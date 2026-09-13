using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Estoque.Api.Features.Pedidos;
using Estoque.Api.Features.Produtos;

namespace Estoque.IntegrationTests;

public sealed class IdempotenciaTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Repetir_com_a_mesma_chave_devolve_o_mesmo_pedido_sem_baixar_o_estoque_de_novo()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);
        var chave = Guid.NewGuid().ToString();

        var primeira = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 2));
        var segunda = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 2));
        var pedidoDaPrimeira = await primeira.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);
        var pedidoDaSegunda = await segunda.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.NotNull(pedidoDaPrimeira);
        Assert.NotNull(pedidoDaSegunda);
        Assert.Equal(pedidoDaPrimeira.Id, pedidoDaSegunda.Id);
        Assert.False(primeira.Headers.Contains(Idempotencia.CabecalhoDeRepeticao));
        Assert.Equal("true", Assert.Single(segunda.Headers.GetValues(Idempotencia.CabecalhoDeRepeticao)));
        Assert.Equal(8, await EstoqueAsync(cliente, produto.Id));
    }

    [Fact]
    public async Task Mesma_chave_com_outro_corpo_retorna_422_sem_criar_pedido()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);
        var chave = Guid.NewGuid().ToString();

        var primeira = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 1));
        var outroCorpo = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 3));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outroCorpo.StatusCode);
        Assert.Equal(9, await EstoqueAsync(cliente, produto.Id));
    }

    // Um cliente que dispara a mesma requisição várias vezes ao mesmo tempo (retry agressivo, clique duplo).
    [Fact]
    public async Task Requisicoes_simultaneas_com_a_mesma_chave_criam_um_unico_pedido()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);
        var chave = Guid.NewGuid().ToString();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CriarPedidoAsync(cliente, chave, Payload(produto.Id, 1))));
        var pedidos = await Task.WhenAll(respostas.Select(r => r.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct)));

        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.Created, resposta.StatusCode));
        Assert.Single(pedidos.Select(p => p?.Id).Distinct());
        Assert.Equal(9, await EstoqueAsync(cliente, produto.Id));
    }

    [Fact]
    public async Task Tentativa_que_falhou_nao_consome_a_chave()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);
        var chave = Guid.NewGuid().ToString();

        var comFreteIndisponivel = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 1, FreteFalso.CepComErro));
        var novaTentativa = await CriarPedidoAsync(cliente, chave, Payload(produto.Id, 1));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, comFreteIndisponivel.StatusCode);
        Assert.Equal(HttpStatusCode.Created, novaTentativa.StatusCode);
        Assert.Equal(9, await EstoqueAsync(cliente, produto.Id));
    }

    [Fact]
    public async Task Sem_a_chave_cada_requisicao_cria_um_pedido()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);

        var primeira = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, 1), Ct);
        var segunda = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, 1), Ct);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(8, await EstoqueAsync(cliente, produto.Id));
    }

    [Fact]
    public async Task Chave_com_mais_de_100_caracteres_retorna_400()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);

        var resposta = await CriarPedidoAsync(cliente, new string('a', 101), Payload(produto.Id, 1));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(10, await EstoqueAsync(cliente, produto.Id));
    }

    private static async Task<HttpResponseMessage> CriarPedidoAsync(HttpClient cliente, string chave, object payload)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/pedidos") { Content = JsonContent.Create(payload) };
        requisicao.Headers.Add(Idempotencia.Cabecalho, chave);
        return await cliente.SendAsync(requisicao, Ct);
    }

    private static object Payload(int produtoId, int quantidade, string cep = "01001000") => new
    {
        clienteNome = "Cliente Idempotente",
        clienteCpf = "52998224725",
        clienteEmail = "idempotente@example.com",
        cep,
        itens = new[] { new { produtoId, quantidade } },
    };

    private static async Task<ProdutoResponse> CriarProdutoAsync(HttpClient cliente, int quantidade)
    {
        var sku = "I" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var resposta = await cliente.PostAsJsonAsync("/api/produtos", new { nome = $"Produto {sku}", sku, preco = 5m, custoUnitario = 1m, quantidade }, Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }

    private static async Task<int> EstoqueAsync(HttpClient cliente, int produtoId)
    {
        var produto = await cliente.GetFromJsonAsync<ProdutoResponse>($"/api/produtos/{produtoId}", Ct);
        Assert.NotNull(produto);
        return produto.Quantidade;
    }
}
