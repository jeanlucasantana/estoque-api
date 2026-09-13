using System.ComponentModel.DataAnnotations;
using Estoque.Api.Common;
using Estoque.Api.Data;
using Estoque.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
        [AsParameters] ParametrosPaginacao paginacao,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var consulta = db.Produtos.Where(p => p.Ativo).OrderBy(p => p.Id);
        return TypedResults.Ok(await PaginarAsync(consulta, paginacao, cancellationToken));
    }

    private static async Task<Ok<ResultadoPaginado<ProdutoResponse>>> Buscar(
        [Required, MaxLength(120)] string nome,
        [AsParameters] ParametrosPaginacao paginacao,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        // ILIKE parametrizado: o termo vai como parâmetro e nunca vira SQL (o legado interpolava e sofria injeção).
        // Os curingas % e _ digitados são escapados para serem procurados como texto literal.
        var padrao = $"%{EscaparCuringas(nome)}%";
        var consulta = db.Produtos
            .Where(p => p.Ativo && EF.Functions.ILike(p.Nome, padrao, @"\"))
            .OrderBy(p => p.Nome)
            .ThenBy(p => p.Id);

        return TypedResults.Ok(await PaginarAsync(consulta, paginacao, cancellationToken));
    }

    // O detalhe mostra também produtos inativos, para consulta do histórico.
    private static async Task<Results<Ok<ProdutoResponse>, ProblemHttpResult>> Obter(
        int id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var produto = await db.Produtos
            .Where(p => p.Id == id)
            .Select(p => new ProdutoResponse(p.Id, p.Nome, p.Sku, p.Preco, p.Quantidade, p.Ativo))
            .FirstOrDefaultAsync(cancellationToken);

        return produto is null ? NaoEncontrado() : TypedResults.Ok(produto);
    }

    private static async Task<Results<Created<ProdutoResponse>, ProblemHttpResult>> Criar(
        SalvarProdutoRequest request,
        AppDbContext db,
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

        logger.LogInformation("Produto {ProdutoId} criado com SKU {Sku}", produto.Id, produto.Sku);
        return TypedResults.Created($"/api/produtos/{produto.Id}", ProdutoResponse.De(produto));
    }

    private static async Task<Results<Ok<ProdutoResponse>, ProblemHttpResult>> Atualizar(
        int id,
        SalvarProdutoRequest request,
        AppDbContext db,
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

        logger.LogInformation("Produto {ProdutoId} atualizado", produto.Id);
        return TypedResults.Ok(ProdutoResponse.De(produto));
    }

    // Exclusão lógica (RN01). O UPDATE direto é atômico e não disputa versão com reservas de estoque em andamento.
    private static async Task<Results<NoContent, ProblemHttpResult>> Remover(
        int id,
        AppDbContext db,
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
