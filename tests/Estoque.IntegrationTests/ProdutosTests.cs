using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Estoque.Api.Common;
using Estoque.Api.Features.Produtos;

namespace Estoque.IntegrationTests;

public sealed class ProdutosTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criar_retorna_201_com_location_e_sem_custo_unitario()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.PostAsJsonAsync("/api/produtos", Payload(NovoSku()), Ct);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        var produto = JsonSerializer.Deserialize<ProdutoResponse>(corpo, JsonSerializerOptions.Web);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.NotNull(produto);
        Assert.Equal($"/api/produtos/{produto.Id}", resposta.Headers.Location?.OriginalString);
        Assert.True(produto.Ativo);
        Assert.DoesNotContain("custo", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Criar_sem_chave_e_com_corpo_invalido_retorna_401_e_nao_400()
    {
        using var cliente = api.CreateClient();

        var resposta = await cliente.PostAsync("/api/produtos", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Sku_duplicado_retorna_409()
    {
        using var cliente = api.CriarClienteAutenticado();
        var sku = NovoSku();
        await CriarAsync(cliente, sku);

        var resposta = await cliente.PostAsJsonAsync("/api/produtos", Payload(sku), Ct);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Theory]
    [InlineData("", "ABC123", "1.00", "0.50", 1)]           // nome vazio
    [InlineData("Parafuso", "abc123", "1.00", "0.50", 1)]   // SKU com minúsculas
    [InlineData("Parafuso", "ABC-123", "1.00", "0.50", 1)]  // SKU com símbolo
    [InlineData("Parafuso", "ABC123", "0", "0.50", 1)]      // preço zero
    [InlineData("Parafuso", "ABC123", "1.005", "0.50", 1)]  // preço com três casas decimais
    [InlineData("Parafuso", "ABC123", "1.00", "-0.01", 1)]  // custo negativo
    [InlineData("Parafuso", "ABC123", "1.00", "0.50", -1)]  // estoque negativo
    public async Task Produto_invalido_retorna_400(string nome, string sku, string preco, string custo, int quantidade)
    {
        using var cliente = api.CriarClienteAutenticado();
        var json = $$"""{"nome":"{{nome}}","sku":"{{sku}}","preco":{{preco}},"custoUnitario":{{custo}},"quantidade":{{quantidade}}}""";

        var resposta = await cliente.PostAsync("/api/produtos", new StringContent(json, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Nome_acima_de_120_ou_sku_acima_de_30_caracteres_retorna_400()
    {
        using var cliente = api.CriarClienteAutenticado();

        var nomeLongo = await cliente.PostAsJsonAsync("/api/produtos", Payload(NovoSku(), nome: new string('a', 121)), Ct);
        var skuLongo = await cliente.PostAsJsonAsync("/api/produtos", Payload(new string('A', 31)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, nomeLongo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, skuLongo.StatusCode);
    }

    [Fact]
    public async Task Obter_produto_inexistente_retorna_404()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.GetAsync($"/api/produtos/{int.MaxValue}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Atualizar_altera_os_dados_e_retorna_200()
    {
        using var cliente = api.CriarClienteAutenticado();
        var sku = NovoSku();
        var produto = await CriarAsync(cliente, sku);

        var resposta = await cliente.PutAsJsonAsync($"/api/produtos/{produto.Id}", Payload(sku, preco: 9.99m, quantidade: 7), Ct);
        var atualizado = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.NotNull(atualizado);
        Assert.Equal(9.99m, atualizado.Preco);
        Assert.Equal(7, atualizado.Quantidade);
    }

    [Fact]
    public async Task Atualizar_produto_inexistente_retorna_404()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.PutAsJsonAsync($"/api/produtos/{int.MaxValue}", Payload(NovoSku()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Atualizar_para_o_sku_de_outro_produto_retorna_409()
    {
        using var cliente = api.CriarClienteAutenticado();
        var outro = await CriarAsync(cliente, NovoSku());
        var produto = await CriarAsync(cliente, NovoSku());

        var resposta = await cliente.PutAsJsonAsync($"/api/produtos/{produto.Id}", Payload(outro.Sku), Ct);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Remover_inativa_o_produto_que_sai_da_busca_mas_continua_existindo()
    {
        using var cliente = api.CriarClienteAutenticado();
        var sku = NovoSku();
        var produto = await CriarAsync(cliente, sku);

        var remocao = await cliente.DeleteAsync($"/api/produtos/{produto.Id}", Ct);
        var busca = await cliente.GetFromJsonAsync<ResultadoPaginado<ProdutoResponse>>($"/api/produtos/busca?nome={sku}", Ct);
        var detalhe = await cliente.GetFromJsonAsync<ProdutoResponse>($"/api/produtos/{produto.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, remocao.StatusCode);
        Assert.NotNull(busca);
        Assert.Empty(busca.Itens);
        Assert.NotNull(detalhe);
        Assert.False(detalhe.Ativo);
    }

    [Fact]
    public async Task Remover_produto_inexistente_retorna_404()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.DeleteAsync($"/api/produtos/{int.MaxValue}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Busca_encontra_por_parte_do_nome_sem_diferenciar_maiusculas()
    {
        using var cliente = api.CriarClienteAutenticado();
        var sku = NovoSku();
        await CriarAsync(cliente, sku);

        var busca = await cliente.GetFromJsonAsync<ResultadoPaginado<ProdutoResponse>>(
            $"/api/produtos/busca?nome={sku.ToLowerInvariant()}", Ct);

        Assert.NotNull(busca);
        Assert.Equal(sku, Assert.Single(busca.Itens).Sku);
    }

    // Critério de aceite 9: o termo é tratado como texto, nunca como SQL nem como curinga.
    [Theory]
    [InlineData("' OR 1=1 --")]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Busca_trata_o_termo_como_texto_literal(string termo)
    {
        using var cliente = api.CriarClienteAutenticado();
        await CriarAsync(cliente, NovoSku());

        var resposta = await cliente.GetAsync($"/api/produtos/busca?nome={Uri.EscapeDataString(termo)}", Ct);
        var busca = await resposta.Content.ReadFromJsonAsync<ResultadoPaginado<ProdutoResponse>>(Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.NotNull(busca);
        Assert.Empty(busca.Itens);
    }

    [Fact]
    public async Task Listagem_segue_o_formato_paginado_do_contrato()
    {
        using var cliente = api.CriarClienteAutenticado();
        await CriarAsync(cliente, NovoSku());

        var pagina = await cliente.GetFromJsonAsync<ResultadoPaginado<ProdutoResponse>>(
            "/api/produtos?pagina=1&tamanhoPagina=1", Ct);

        Assert.NotNull(pagina);
        Assert.Single(pagina.Itens);
        Assert.Equal(1, pagina.Pagina);
        Assert.Equal(1, pagina.TamanhoPagina);
        Assert.True(pagina.Total >= 1);
    }

    // Critério de aceite 10 e os demais limites de paginação.
    [Theory]
    [InlineData("/api/produtos?tamanhoPagina=1000")]
    [InlineData("/api/produtos?tamanhoPagina=0")]
    [InlineData("/api/produtos?pagina=0")]
    [InlineData("/api/produtos/busca?nome=a&tamanhoPagina=101")]
    [InlineData("/api/produtos/busca")]
    public async Task Parametros_invalidos_retornam_400(string rota)
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.GetAsync(rota, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    private static string NovoSku() => "T" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();

    private static object Payload(string sku, string? nome = null, decimal preco = 10.50m, int quantidade = 10) =>
        new { nome = nome ?? $"Produto de teste {sku}", sku, preco, custoUnitario = 4.20m, quantidade };

    private static async Task<ProdutoResponse> CriarAsync(HttpClient cliente, string sku)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/produtos", Payload(sku), Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }
}
