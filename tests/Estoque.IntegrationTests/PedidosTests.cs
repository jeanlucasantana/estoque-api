using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Estoque.Api.Common;
using Estoque.Api.Domain;
using Estoque.Api.Features.Pedidos;
using Estoque.Api.Features.Produtos;

namespace Estoque.IntegrationTests;

public sealed class PedidosTests(ApiFactory api)
{
    private const string CpfValido = "52998224725";
    private const string EmailDoCliente = "maria@example.com";
    private const string CepPadrao = "01001000";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criar_pedido_calcula_valores_baixa_estoque_e_nao_expoe_dados_pessoais()
    {
        using var cliente = api.CriarClienteAutenticado();
        var parafuso = await CriarProdutoAsync(cliente, preco: 0.35m, quantidade: 5000);
        var furadeira = await CriarProdutoAsync(cliente, preco: 289.90m, quantidade: 12);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((parafuso.Id, 100), (furadeira.Id, 1)), Ct);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        var pedido = JsonSerializer.Deserialize<PedidoResponse>(corpo, Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.NotNull(pedido);
        Assert.Equal($"/api/pedidos/{pedido.Id}", resposta.Headers.Location?.OriginalString);
        Assert.Equal(StatusPedido.Novo, pedido.Status);
        Assert.Equal(324.90m, pedido.Subtotal);
        Assert.Equal(0m, pedido.Desconto);
        Assert.Equal(25.90m, pedido.Frete);
        Assert.Equal(350.80m, pedido.Total);
        Assert.Equal("25", pedido.Cliente.CpfFinal);
        Assert.DoesNotContain(CpfValido, corpo);
        Assert.DoesNotContain(EmailDoCliente, corpo);
        Assert.Equal(4900, await EstoqueAsync(cliente, parafuso.Id));
        Assert.Equal(11, await EstoqueAsync(cliente, furadeira.Id));
    }

    [Fact]
    public async Task Detalhe_mostra_cpf_mascarado_e_nao_mostra_email()
    {
        using var cliente = api.CriarClienteAutenticado();
        var pedido = await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 1);

        var corpo = await cliente.GetStringAsync($"/api/pedidos/{pedido.Id}", Ct);
        var detalhe = JsonSerializer.Deserialize<PedidoResponse>(corpo, Json);

        Assert.NotNull(detalhe);
        Assert.Equal("25", detalhe.Cliente.CpfFinal);
        Assert.DoesNotContain(CpfValido, corpo);
        Assert.DoesNotContain(EmailDoCliente, corpo);
    }

    [Fact]
    public async Task Itens_repetidos_do_mesmo_produto_sao_consolidados()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((produto.Id, 2), (produto.Id, 3)), Ct);
        var pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.NotNull(pedido);
        Assert.Equal(5, Assert.Single(pedido.Itens).Quantidade);
        Assert.Equal(5, await EstoqueAsync(cliente, produto.Id));
    }

    // Critério de aceite 3.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Item_com_quantidade_zero_ou_negativa_retorna_400_e_nao_altera_o_estoque(int quantidade)
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((produto.Id, quantidade)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(10, await EstoqueAsync(cliente, produto.Id));
    }

    [Theory]
    [InlineData("52998224724", EmailDoCliente, CepPadrao)]   // CPF com dígito verificador errado
    [InlineData(CpfValido, "maria.example.com", CepPadrao)]  // email sem @
    [InlineData(CpfValido, EmailDoCliente, "0100100")]       // CEP com 7 dígitos
    [InlineData(CpfValido, EmailDoCliente, "01001-000")]     // CEP com hífen
    public async Task Dados_do_cliente_invalidos_retornam_400(string cpf, string email, string cep)
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);
        var payload = new { clienteNome = "Maria Souza", clienteCpf = cpf, clienteEmail = email, cep, itens = new[] { new { produtoId = produto.Id, quantidade = 1 } } };

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", payload, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(10, await EstoqueAsync(cliente, produto.Id));
    }

    [Fact]
    public async Task Pedido_sem_itens_retorna_400()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload(), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Produto_inexistente_ou_inativo_retorna_400_identificando_o_produto()
    {
        using var cliente = api.CriarClienteAutenticado();
        var inativo = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);
        (await cliente.DeleteAsync($"/api/produtos/{inativo.Id}", Ct)).EnsureSuccessStatusCode();
        const int inexistente = int.MaxValue;

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((inativo.Id, 1), (inexistente, 1)), Ct);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains($"produto {inativo.Id}", corpo);
        Assert.Contains($"produto {inexistente}", corpo);
        Assert.Equal(10, await EstoqueAsync(cliente, inativo.Id));
    }

    [Fact]
    public async Task Estoque_insuficiente_retorna_409_listando_os_produtos_e_nao_reserva_nenhum_item()
    {
        using var cliente = api.CriarClienteAutenticado();
        var comSaldo = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);
        var semSaldo = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 2);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((comSaldo.Id, 5), (semSaldo.Id, 3)), Ct);
        var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal([semSaldo.Id], problema.GetProperty("produtos").EnumerateArray().Select(p => p.GetInt32()));
        Assert.Equal(10, await EstoqueAsync(cliente, comSaldo.Id));
        Assert.Equal(2, await EstoqueAsync(cliente, semSaldo.Id));
    }

    // Critério de aceite 5.
    [Fact]
    public async Task Frete_lento_retorna_503_em_cerca_de_2_segundos_sem_alterar_o_estoque()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);
        var cronometro = Stopwatch.StartNew();

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", PayloadComCep(FreteFalso.CepLento, (produto.Id, 1)), Ct);
        cronometro.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.InRange(cronometro.Elapsed, TimeSpan.FromSeconds(1.8), TimeSpan.FromSeconds(4));
        Assert.Equal(10, await EstoqueAsync(cliente, produto.Id));
    }

    // Critério de aceite 6 e resposta fora do contrato.
    [Theory]
    [InlineData(FreteFalso.CepComErro)]
    [InlineData(FreteFalso.CepComRespostaInvalida)]
    public async Task Falha_ou_resposta_invalida_do_frete_retorna_503_sem_alterar_o_estoque(string cep)
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 1m, quantidade: 10);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", PayloadComCep(cep, (produto.Id, 1)), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Equal(10, await EstoqueAsync(cliente, produto.Id));
    }

    // Critério de aceite 4: prova que requisições simultâneas não vendem acima do estoque.
    [Fact]
    public async Task Trinta_pedidos_simultaneos_para_dez_unidades_vendem_exatamente_dez()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, preco: 10m, quantidade: 10);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => cliente.PostAsJsonAsync("/api/pedidos", Payload((produto.Id, 1)), Ct)));

        Assert.Equal(10, respostas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(20, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await EstoqueAsync(cliente, produto.Id));
    }

    // Critério de aceite 7.
    [Fact]
    public async Task Cancelar_pedido_novo_devolve_o_estoque_e_cancelar_de_novo_retorna_409()
    {
        using var cliente = api.CriarClienteAutenticado();
        var pedido = await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 4);
        var produtoId = Assert.Single(pedido.Itens).ProdutoId;
        Assert.Equal(6, await EstoqueAsync(cliente, produtoId));

        var primeiro = await cliente.PostAsync($"/api/pedidos/{pedido.Id}/cancelamento", content: null, Ct);
        var segundo = await cliente.PostAsync($"/api/pedidos/{pedido.Id}/cancelamento", content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal(10, await EstoqueAsync(cliente, produtoId));
    }

    [Fact]
    public async Task Cancelamentos_simultaneos_devolvem_o_estoque_uma_unica_vez()
    {
        using var cliente = api.CriarClienteAutenticado();
        var pedido = await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 3);
        var produtoId = Assert.Single(pedido.Itens).ProdutoId;

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => cliente.PostAsync($"/api/pedidos/{pedido.Id}/cancelamento", content: null, Ct)));

        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(10, await EstoqueAsync(cliente, produtoId));
    }

    [Fact]
    public async Task Ciclo_de_vida_segue_novo_pago_enviado_e_recusa_outras_transicoes()
    {
        using var cliente = api.CriarClienteAutenticado();
        var pedido = await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 1);
        var rota = $"/api/pedidos/{pedido.Id}";

        var envioAntesDoPagamento = await cliente.PostAsync($"{rota}/envio", content: null, Ct);
        var pagamento = await cliente.PostAsync($"{rota}/pagamento", content: null, Ct);
        var pagamentoRepetido = await cliente.PostAsync($"{rota}/pagamento", content: null, Ct);
        var envio = await cliente.PostAsync($"{rota}/envio", content: null, Ct);
        var cancelamentoDeEnviado = await cliente.PostAsync($"{rota}/cancelamento", content: null, Ct);
        var final = await cliente.GetFromJsonAsync<PedidoResponse>(rota, Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, envioAntesDoPagamento.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pagamento.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, pagamentoRepetido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, envio.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelamentoDeEnviado.StatusCode);
        Assert.NotNull(final);
        Assert.Equal(StatusPedido.Enviado, final.Status);
    }

    [Theory]
    [InlineData("pagamento")]
    [InlineData("envio")]
    [InlineData("cancelamento")]
    public async Task Transicao_de_pedido_inexistente_retorna_404(string acao)
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.PostAsync($"/api/pedidos/{int.MaxValue}/{acao}", content: null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // Critério de aceite 8.
    [Fact]
    public async Task Listagem_nao_expoe_cpf_nem_email_e_filtra_por_status()
    {
        using var cliente = api.CriarClienteAutenticado();
        var pago = await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 1);
        (await cliente.PostAsync($"/api/pedidos/{pago.Id}/pagamento", content: null, Ct)).EnsureSuccessStatusCode();
        await CriarPedidoAsync(cliente, quantidadeEmEstoque: 10, quantidadePedida: 1);

        var corpo = await cliente.GetStringAsync("/api/pedidos?pagina=1&tamanhoPagina=100&status=Pago", Ct);
        var pagina = JsonSerializer.Deserialize<ResultadoPaginado<PedidoResumoResponse>>(corpo, Json);

        Assert.DoesNotContain(CpfValido, corpo);
        Assert.DoesNotContain(EmailDoCliente, corpo);
        Assert.NotNull(pagina);
        Assert.Contains(pagina.Itens, p => p.Id == pago.Id);
        Assert.All(pagina.Itens, p => Assert.Equal(StatusPedido.Pago, p.Status));
    }

    [Theory]
    [InlineData("/api/pedidos?tamanhoPagina=1000")]
    [InlineData("/api/pedidos?pagina=0")]
    [InlineData("/api/pedidos?status=Inexistente")]
    public async Task Parametros_invalidos_da_listagem_retornam_400(string rota)
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.GetAsync(rota, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Obter_pedido_inexistente_retorna_404()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.GetAsync($"/api/pedidos/{int.MaxValue}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    private static object Payload(params (int ProdutoId, int Quantidade)[] itens) => PayloadComCep(CepPadrao, itens);

    private static object PayloadComCep(string cep, params (int ProdutoId, int Quantidade)[] itens) => new
    {
        clienteNome = "Maria Souza",
        clienteCpf = CpfValido,
        clienteEmail = EmailDoCliente,
        cep,
        itens = itens.Select(i => new { produtoId = i.ProdutoId, quantidade = i.Quantidade }),
    };

    private static async Task<ProdutoResponse> CriarProdutoAsync(HttpClient cliente, decimal preco, int quantidade)
    {
        var sku = "P" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var payload = new { nome = $"Produto de pedido {sku}", sku, preco, custoUnitario = 0m, quantidade };

        var resposta = await cliente.PostAsJsonAsync("/api/produtos", payload, Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }

    private static async Task<PedidoResponse> CriarPedidoAsync(HttpClient cliente, int quantidadeEmEstoque, int quantidadePedida)
    {
        var produto = await CriarProdutoAsync(cliente, preco: 5m, quantidade: quantidadeEmEstoque);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload((produto.Id, quantidadePedida)), Ct);
        resposta.EnsureSuccessStatusCode();

        var pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);
        Assert.NotNull(pedido);
        return pedido;
    }

    private static async Task<int> EstoqueAsync(HttpClient cliente, int produtoId)
    {
        var produto = await cliente.GetFromJsonAsync<ProdutoResponse>($"/api/produtos/{produtoId}", Ct);
        Assert.NotNull(produto);
        return produto.Quantidade;
    }
}
