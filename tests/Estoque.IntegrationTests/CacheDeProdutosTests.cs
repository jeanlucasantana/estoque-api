using System.Net.Http.Json;
using System.Text.Json;
using Estoque.Api.Features.Produtos;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Estoque.IntegrationTests;

public sealed class CacheDeProdutosTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Consulta_do_produto_fica_em_cache()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente);

        var consultado = await ObterAsync(cliente, produto.Id);
        var fabricaChamada = false;
        var emCache = await api.Services.GetRequiredService<HybridCache>().GetOrCreateAsync<ProdutoResponse?>(
            CacheDeProdutos.Chave(produto.Id),
            _ =>
            {
                fabricaChamada = true;
                return ValueTask.FromResult<ProdutoResponse?>(null);
            },
            cancellationToken: Ct);

        Assert.False(fabricaChamada);
        Assert.Equal(consultado, emCache);
    }

    // O legado tinha um cache que nunca era invalidado (DIAGNOSTICO C2). Aqui cada escrita remove a entrada.
    [Fact]
    public async Task Toda_escrita_que_muda_o_produto_invalida_o_cache()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente);
        Assert.Equal(10, (await ObterAsync(cliente, produto.Id)).Quantidade);

        (await cliente.PutAsJsonAsync(
            $"/api/produtos/{produto.Id}",
            new { nome = produto.Nome, sku = produto.Sku, preco = 12m, custoUnitario = 1m, quantidade = 10 },
            Ct)).EnsureSuccessStatusCode();
        Assert.Equal(12m, (await ObterAsync(cliente, produto.Id)).Preco);

        var pedido = await cliente.PostAsJsonAsync("/api/pedidos", new
        {
            clienteNome = "Cliente Cache",
            clienteCpf = "52998224725",
            clienteEmail = "cache@example.com",
            cep = "01001000",
            itens = new[] { new { produtoId = produto.Id, quantidade = 3 } },
        }, Ct);
        pedido.EnsureSuccessStatusCode();
        var pedidoId = (await pedido.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt32();
        Assert.Equal(7, (await ObterAsync(cliente, produto.Id)).Quantidade);

        (await cliente.PostAsync($"/api/pedidos/{pedidoId}/cancelamento", content: null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(10, (await ObterAsync(cliente, produto.Id)).Quantidade);

        (await cliente.DeleteAsync($"/api/produtos/{produto.Id}", Ct)).EnsureSuccessStatusCode();
        Assert.False((await ObterAsync(cliente, produto.Id)).Ativo);
    }

    private static async Task<ProdutoResponse> CriarProdutoAsync(HttpClient cliente)
    {
        var sku = "K" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var resposta = await cliente.PostAsJsonAsync(
            "/api/produtos", new { nome = $"Produto cache {sku}", sku, preco = 10m, custoUnitario = 1m, quantidade = 10 }, Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }

    private static async Task<ProdutoResponse> ObterAsync(HttpClient cliente, int produtoId)
    {
        var produto = await cliente.GetFromJsonAsync<ProdutoResponse>($"/api/produtos/{produtoId}", Ct);
        Assert.NotNull(produto);
        return produto;
    }
}
