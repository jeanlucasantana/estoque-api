using System.ComponentModel.DataAnnotations;
using Estoque.Api.Common;
using Estoque.Api.Data;
using Estoque.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Estoque.Api.Features.Pedidos;

public static class PedidosEndpoints
{
    public static void MapPedidos(this IEndpointRouteBuilder api)
    {
        var pedidos = api.MapGroup("/pedidos").WithTags("Pedidos");

        pedidos.MapGet("/", Listar);
        pedidos.MapGet("/{id:int}", Obter);
        pedidos.MapPost("/", Criar);
        pedidos.MapPost("/{id:int}/pagamento", Pagar);
        pedidos.MapPost("/{id:int}/envio", Enviar);
        pedidos.MapPost("/{id:int}/cancelamento", Cancelar);
    }

    private static async Task<Ok<ResultadoPaginado<PedidoResumoResponse>>> Listar(
        [Range(1, ParametrosPaginacao.PaginaMaxima, ErrorMessage = ParametrosPaginacao.MensagemPagina)] int? pagina,
        [Range(1, ParametrosPaginacao.TamanhoMaximo, ErrorMessage = ParametrosPaginacao.MensagemTamanho)] int? tamanhoPagina,
        StatusPedido? status,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var paginacao = ParametrosPaginacao.De(pagina, tamanhoPagina);
        var consulta = db.Pedidos.AsQueryable();
        if (status is not null)
        {
            consulta = consulta.Where(p => p.Status == status);
        }

        var total = await consulta.CountAsync(cancellationToken);
        var linhas = await consulta
            .OrderByDescending(p => p.Id)
            .Skip(paginacao.Deslocamento)
            .Take(paginacao.TamanhoPagina)
            .Select(p => new { p.Id, p.Status, p.CriadoEm, p.ClienteNome, QuantidadeItens = p.Itens.Count, p.Total })
            .ToListAsync(cancellationToken);

        var itens = linhas
            .Select(p => new PedidoResumoResponse(p.Id, p.Status, p.CriadoEm.UtcDateTime, p.ClienteNome, p.QuantidadeItens, p.Total))
            .ToList();

        return TypedResults.Ok(new ResultadoPaginado<PedidoResumoResponse>(itens, paginacao.Pagina, paginacao.TamanhoPagina, total));
    }

    private static async Task<Results<Ok<PedidoResponse>, ProblemHttpResult>> Obter(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return pedido is null ? NaoEncontrado() : TypedResults.Ok(PedidoResponse.De(pedido));
    }

    private static async Task<Results<Created<PedidoResponse>, ValidationProblem, ProblemHttpResult>> Criar(
        CriarPedidoRequest request,
        AppDbContext db,
        ServicoFrete servicoFrete,
        TimeProvider relogio,
        FilaDeConfirmacoes confirmacoes,
        ILogger<Pedido> logger,
        CancellationToken cancellationToken)
    {
        // RN02: itens repetidos do mesmo produto são consolidados em um só. A ordem por id é a ordem da reserva.
        var quantidades = request.Itens
            .GroupBy(i => i.ProdutoId)
            .OrderBy(g => g.Key)
            .Select(g => (ProdutoId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .ToList();

        var ids = quantidades.Select(q => q.ProdutoId).ToArray();
        var produtos = await db.Produtos
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Preco, p.Ativo })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var indisponiveis = ids.Where(id => !produtos.TryGetValue(id, out var produto) || !produto.Ativo).ToList();
        if (indisponiveis.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Itens)] = [.. indisponiveis.Select(id => $"O produto {id} não existe ou está inativo.")],
            });
        }

        // RN04: o preço vigente agora é o preço gravado no item.
        var itens = quantidades
            .Select(q => new ItemPedido(q.ProdutoId, produtos[q.ProdutoId].Sku, q.Quantidade, produtos[q.ProdutoId].Preco))
            .ToList();
        var subtotal = CalculadoraPedido.CalcularSubtotal(itens.Select(i => new ItemCalculo(i.PrecoUnitario, i.Quantidade)));

        // RN05: o frete é consultado antes de abrir a transação, para não segurar travas durante uma chamada externa.
        var frete = await servicoFrete.CalcularAsync(request.Cep, subtotal, cancellationToken);
        if (frete is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Serviço de frete indisponível.",
                detail: "O pedido não foi criado e nenhum estoque foi reservado. Tente novamente em instantes.");
        }

        var agora = relogio.GetUtcNow();
        var pedido = new Pedido(
            request.ClienteNome,
            request.ClienteCpf,
            request.ClienteEmail,
            request.Cep,
            itens,
            CalculadoraPedido.Calcular(subtotal, frete.Value, agora),
            agora);

        await using (var transacao = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var semEstoque = await ReservarEstoqueAsync(db, itens, cancellationToken);
            if (semEstoque.Count > 0)
            {
                // Sai sem commit: o descarte da transação desfaz as reservas que já tinham dado certo (RN03, tudo ou nada).
                logger.LogInformation("Pedido recusado por estoque insuficiente nos produtos {ProdutoIds}", semEstoque);
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Estoque insuficiente.",
                    detail: "Nenhum item foi reservado. Veja em 'produtos' quais não têm quantidade suficiente.",
                    extensions: new Dictionary<string, object?> { ["produtos"] = semEstoque });
            }

            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }

        // RN08: enfileira depois do commit, para nunca confirmar um pedido que não foi gravado.
        confirmacoes.Enfileirar(pedido.Id);

        logger.LogInformation("Pedido {PedidoId} criado com {QuantidadeItens} itens e total {Total}", pedido.Id, itens.Count, pedido.Total);
        return TypedResults.Created($"/api/pedidos/{pedido.Id}", PedidoResponse.De(pedido));
    }

    // RN03. Um UPDATE condicional por produto: conferir o saldo e baixar acontecem no mesmo comando.
    // O PostgreSQL trava a linha, e um UPDATE concorrente espera e reavalia o WHERE com o saldo já atualizado.
    // Como a trava está no banco, funciona com qualquer número de réplicas. A ordem crescente de id evita deadlock
    // entre pedidos que disputam os mesmos produtos.
    private static async Task<List<int>> ReservarEstoqueAsync(
        AppDbContext db,
        IReadOnlyList<ItemPedido> itensEmOrdemDeProduto,
        CancellationToken cancellationToken)
    {
        var semEstoque = new List<int>();

        foreach (var item in itensEmOrdemDeProduto)
        {
            var reservados = await db.Produtos
                .Where(p => p.Id == item.ProdutoId && p.Ativo && p.Quantidade >= item.Quantidade)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Quantidade, p => p.Quantidade - item.Quantidade), cancellationToken);

            // Continua mesmo após uma falha, para informar de uma vez todos os produtos sem saldo.
            if (reservados == 0)
            {
                semEstoque.Add(item.ProdutoId);
            }
        }

        return semEstoque;
    }

    private static Task<Results<Ok<PedidoResponse>, ProblemHttpResult>> Pagar(
        int id,
        AppDbContext db,
        TimeProvider relogio,
        ILogger<Pedido> logger,
        CancellationToken cancellationToken) =>
        AlterarStatusAsync(id, db, logger, pedido => pedido.RegistrarPagamento(relogio.GetUtcNow()), cancellationToken);

    private static Task<Results<Ok<PedidoResponse>, ProblemHttpResult>> Enviar(
        int id,
        AppDbContext db,
        TimeProvider relogio,
        ILogger<Pedido> logger,
        CancellationToken cancellationToken) =>
        AlterarStatusAsync(id, db, logger, pedido => pedido.RegistrarEnvio(relogio.GetUtcNow()), cancellationToken);

    private static async Task<Results<Ok<PedidoResponse>, ProblemHttpResult>> AlterarStatusAsync(
        int id,
        AppDbContext db,
        ILogger logger,
        Func<Pedido, bool> transicao,
        CancellationToken cancellationToken)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pedido is null)
        {
            return NaoEncontrado();
        }

        var statusAnterior = pedido.Status;
        if (!transicao(pedido))
        {
            return TransicaoInvalida(statusAnterior);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AlteradoPorOutraRequisicao();
        }

        logger.LogInformation("Pedido {PedidoId} passou de {StatusAnterior} para {StatusAtual}", pedido.Id, statusAnterior, pedido.Status);
        return TypedResults.Ok(PedidoResponse.De(pedido));
    }

    // RN06: cancelar devolve ao estoque as quantidades reservadas, na mesma transação da mudança de status.
    private static async Task<Results<Ok<PedidoResponse>, ProblemHttpResult>> Cancelar(
        int id,
        AppDbContext db,
        TimeProvider relogio,
        ILogger<Pedido> logger,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pedido is null)
        {
            return NaoEncontrado();
        }

        var statusAnterior = pedido.Status;
        if (!pedido.Cancelar(relogio.GetUtcNow()))
        {
            return TransicaoInvalida(statusAnterior);
        }

        try
        {
            // O status é gravado antes da devolução. Com o xmin, em dois cancelamentos simultâneos só um passa daqui:
            // o outro recebe 409 antes de tocar no estoque, que volta uma única vez.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AlteradoPorOutraRequisicao();
        }

        foreach (var item in pedido.Itens.OrderBy(i => i.ProdutoId))
        {
            await db.Produtos
                .Where(p => p.Id == item.ProdutoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Quantidade, p => p.Quantidade + item.Quantidade), cancellationToken);
        }

        await transacao.CommitAsync(cancellationToken);

        logger.LogInformation("Pedido {PedidoId} cancelado com devolução de estoque de {QuantidadeItens} itens", pedido.Id, pedido.Itens.Count);
        return TypedResults.Ok(PedidoResponse.De(pedido));
    }

    private static ProblemHttpResult NaoEncontrado() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Pedido não encontrado.");

    private static ProblemHttpResult TransicaoInvalida(StatusPedido statusAtual) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Transição de status não permitida.",
            detail: $"O pedido está com status {statusAtual}.");

    private static ProblemHttpResult AlteradoPorOutraRequisicao() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "O pedido foi alterado por outra requisição.",
            detail: "Consulte o pedido novamente antes de tentar outra vez.");
}
