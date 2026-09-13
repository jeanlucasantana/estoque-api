using System.ComponentModel.DataAnnotations;
using Estoque.Api.Common;
using Estoque.Api.Data;
using Estoque.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Npgsql;

namespace Estoque.Api.Features.Produtos;

public static class ProdutosEndpoints
{
    public static void MapProdutos(this IEndpointRouteBuilder api)
    {
        var produtos = api.MapGroup("/produtos").WithTags("Produtos");

        produtos.MapGet("/", Listar);
        produtos.MapGet("/busca", Buscar);
        produtos.MapGet("/{id:int}", Obter);
        produtos.MapPost("/", Criar);
        produtos.MapPut("/{id:int}", Atualizar);
        produtos.MapDelete("/{id:int}", Remover);
    }

    // A listagem padrão mostra só produtos ativos (RN01).
    private static async Task<Ok<ResultadoPaginado<ProdutoResponse>>> Listar(
        [Range(1, ParametrosPaginacao.PaginaMaxima, ErrorMessage = ParametrosPaginacao.MensagemPagina)] int? pagina,
        [Range(1, ParametrosPaginacao.TamanhoMaximo, ErrorMessage = ParametrosPaginacao.MensagemTamanho)] int? tamanhoPagina,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var consulta = db.Produtos.Where(p => p.Ativo).OrderBy(p => p.Id);
        return TypedResults.Ok(await PaginarAsync(consulta, ParametrosPaginacao.De(pagina, tamanhoPagina), cancellationToken));
    }

    private static async Task<Ok<ResultadoPaginado<ProdutoResponse>>> Buscar(
        [Required, MaxLength(120)] string nome,
        [Range(1, ParametrosPaginacao.PaginaMaxima, ErrorMessage = ParametrosPaginacao.MensagemPagina)] int? pagina,
        [Range(1, ParametrosPaginacao.TamanhoMaximo, ErrorMessage = ParametrosPaginacao.MensagemTamanho)] int? tamanhoPagina,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var paginacao = ParametrosPaginacao.De(pagina, tamanhoPagina);

        // ILIKE parametrizado: o termo vai como parâmetro e nunca vira SQL (o legado interpolava e sofria injeção).
        // Os curingas % e _ digitados são escapados para serem procurados como texto literal.
        var padrao = $"%{EscaparCuringas(nome)}%";
        var consulta = db.Produtos
            .Where(p => p.Ativo && EF.Functions.ILike(p.Nome, padrao, @"\"))
            .OrderBy(p => p.Nome)
            .ThenBy(p => p.Id);

        return TypedResults.Ok(await PaginarAsync(consulta, paginacao, cancellationToken));
    }

    // O detalhe mostra também produtos inativos, para consulta do histórico. A leitura passa pelo cache
    // (CacheDeProdutos); um id inexistente também fica em cache, e a criação do produto remove essa entrada.
    private static async Task<Results<Ok<ProdutoResponse>, ProblemHttpResult>> Obter(
        int id,
        AppDbContext db,
        HybridCache cache,
        CancellationToken cancellationToken)
    {
        var produto = await cache.GetOrCreateAsync<ProdutoResponse?>(
            CacheDeProdutos.Chave(id),
            async token => await db.Produtos
                .Where(p => p.Id == id)
                .Select(p => new ProdutoResponse(p.Id, p.Nome, p.Sku, p.Preco, p.Quantidade, p.Ativo))
                .FirstOrDefaultAsync(token),
            CacheDeProdutos.Opcoes,
            cancellationToken: cancellationToken);

        return produto is null ? NaoEncontrado() : TypedResults.Ok(produto);
    }

    private static async Task<Results<Created<ProdutoResponse>, ProblemHttpResult>> Criar(
        SalvarProdutoRequest request,
        AppDbContext db,
        HybridCache cache,
        ILogger<Produto> logger,
        CancellationToken cancellationToken)
    {
        var produto = new Produto(request.Nome, request.Sku, request.Preco, request.CustoUnitario, request.Quantidade);
        db.Produtos.Add(produto);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException erro) when (EhSkuDuplicado(erro))
        {
            return SkuDuplicado();
        }

        await CacheDeProdutos.InvalidarAsync(cache, [produto.Id], cancellationToken);

        logger.LogInformation("Produto {ProdutoId} criado com SKU {Sku}", produto.Id, produto.Sku);
        return TypedResults.Created($"/api/produtos/{produto.Id}", ProdutoResponse.De(produto));
    }

    private static async Task<Results<Ok<ProdutoResponse>, ProblemHttpResult>> Atualizar(
        int id,
        SalvarProdutoRequest request,
        AppDbContext db,
        HybridCache cache,
        ILogger<Produto> logger,
        CancellationToken cancellationToken)
    {
        var produto = await db.Produtos.FindAsync([id], cancellationToken);
        if (produto is null)
        {
            return NaoEncontrado();
        }

        produto.Atualizar(request.Nome, request.Sku, request.Preco, request.CustoUnitario, request.Quantidade);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // O xmin mudou desde a leitura: uma reserva de estoque ou outro PUT alterou o produto no meio do caminho.
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "O produto foi alterado por outra requisição. Consulte-o novamente e repita a atualização.");
        }
        catch (DbUpdateException erro) when (EhSkuDuplicado(erro))
        {
            return SkuDuplicado();
        }

        await CacheDeProdutos.InvalidarAsync(cache, [produto.Id], cancellationToken);

        logger.LogInformation("Produto {ProdutoId} atualizado", produto.Id);
        return TypedResults.Ok(ProdutoResponse.De(produto));
    }

    // Exclusão lógica (RN01). O UPDATE direto é atômico e não disputa versão com reservas de estoque em andamento.
    private static async Task<Results<NoContent, ProblemHttpResult>> Remover(
        int id,
        AppDbContext db,
        HybridCache cache,
        ILogger<Produto> logger,
        CancellationToken cancellationToken)
    {
        var alterados = await db.Produtos
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Ativo, false), cancellationToken);

        if (alterados == 0)
        {
            return NaoEncontrado();
        }

        await CacheDeProdutos.InvalidarAsync(cache, [id], cancellationToken);

        logger.LogInformation("Produto {ProdutoId} inativado", id);
        return TypedResults.NoContent();
    }

    private static async Task<ResultadoPaginado<ProdutoResponse>> PaginarAsync(
        IQueryable<Produto> consulta,
        ParametrosPaginacao paginacao,
        CancellationToken cancellationToken)
    {
        var total = await consulta.CountAsync(cancellationToken);

        // A projeção busca só as colunas da resposta: o custo nem sai do banco.
        var itens = await consulta
            .Skip(paginacao.Deslocamento)
            .Take(paginacao.TamanhoPagina)
            .Select(p => new ProdutoResponse(p.Id, p.Nome, p.Sku, p.Preco, p.Quantidade, p.Ativo))
            .ToListAsync(cancellationToken);

        return new ResultadoPaginado<ProdutoResponse>(itens, paginacao.Pagina, paginacao.TamanhoPagina, total);
    }

    private static bool EhSkuDuplicado(DbUpdateException erro) =>
        erro.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_produtos_sku" };

    private static string EscaparCuringas(string termo) =>
        termo.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static ProblemHttpResult NaoEncontrado() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Produto não encontrado.");

    private static ProblemHttpResult SkuDuplicado() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Já existe um produto com este SKU.");
}
