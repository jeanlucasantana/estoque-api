using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Estoque.Api.Data;
using Estoque.Api.Features.Pedidos;
using Estoque.Api.Features.Produtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Estoque.IntegrationTests;

public sealed class OutboxTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_criado_grava_a_confirmacao_pendente_e_o_processador_a_marca_como_enviada()
    {
        using var cliente = api.CriarClienteAutenticado();

        var pedidoId = await CriarPedidoAsync(cliente);
        await EsperarAsync(db => db.ConfirmacoesPendentes.AnyAsync(c => c.PedidoId == pedidoId && c.EnviadaEm != null, Ct));

        await using var escopo = api.Services.CreateAsyncScope();
        var confirmacao = await escopo.ServiceProvider.GetRequiredService<AppDbContext>()
            .ConfirmacoesPendentes.AsNoTracking().SingleAsync(c => c.PedidoId == pedidoId, Ct);
        Assert.Equal(0, confirmacao.Tentativas);
        Assert.Equal(1, ContarEnvios(api, pedidoId));
    }

    // Simula uma instância que gravou o pedido e parou antes de enviar a confirmação: a confirmação está no banco,
    // e outra instância a envia. Com a fila em memória, ela teria se perdido.
    [Fact]
    public async Task Confirmacao_gravada_por_uma_instancia_sem_processador_e_enviada_por_outra()
    {
        using var instanciaParada = api.WithWebHostBuilder(builder => builder.UseSetting("Confirmacoes:Habilitado", "false"));
        using var cliente = CriarCliente(instanciaParada);

        var pedidoId = await CriarPedidoAsync(cliente);
        await EsperarAsync(db => db.ConfirmacoesPendentes.AnyAsync(c => c.PedidoId == pedidoId && c.EnviadaEm != null, Ct));

        Assert.Equal(0, ContarEnvios(instanciaParada, pedidoId));
        Assert.Equal(1, ContarEnvios(api, pedidoId));
    }

    // Duas instâncias processando o mesmo outbox ao mesmo tempo: FOR UPDATE SKIP LOCKED impede o envio duplicado.
    [Fact]
    public async Task Duas_instancias_processando_o_mesmo_outbox_enviam_cada_confirmacao_uma_unica_vez()
    {
        using var instanciaSemProcessador = api.WithWebHostBuilder(builder => builder.UseSetting("Confirmacoes:Habilitado", "false"));
        using var segundaInstancia = api.WithWebHostBuilder(builder => builder.UseSetting("Confirmacoes:TamanhoDoLote", "1"));
        _ = segundaInstancia.Services;
        using var cliente = CriarCliente(instanciaSemProcessador);

        var pedidos = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => CriarPedidoAsync(cliente)));
        await EsperarAsync(async db => await db.ConfirmacoesPendentes.CountAsync(c => pedidos.Contains(c.PedidoId) && c.EnviadaEm != null, Ct) == pedidos.Length);

        Assert.All(pedidos, pedidoId => Assert.Equal(1, ContarEnvios(api, pedidoId) + ContarEnvios(segundaInstancia, pedidoId)));
    }

    private static HttpClient CriarCliente(WebApplicationFactory<Program> fabrica)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ChaveApi);
        return cliente;
    }

    private static int ContarEnvios(WebApplicationFactory<Program> fabrica, int pedidoId)
    {
        var id = pedidoId.ToString(CultureInfo.InvariantCulture);
        return fabrica.Services.GetFakeLogCollector().GetSnapshot().Count(registro =>
            registro.Category == typeof(ProcessadorDeConfirmacoes).FullName
            && registro.Message.Contains("enviada", StringComparison.Ordinal)
            && registro.StructuredState?.Any(par => par.Key == "PedidoId" && par.Value == id) == true);
    }

    // Consulta o banco até a condição ser verdadeira; o limite só evita que uma falha trave a execução dos testes.
    private async Task EsperarAsync(Func<AppDbContext, Task<bool>> condicao)
    {
        var cronometro = Stopwatch.StartNew();
        while (true)
        {
            await using var escopo = api.Services.CreateAsyncScope();
            if (await condicao(escopo.ServiceProvider.GetRequiredService<AppDbContext>()))
            {
                return;
            }

            Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(20), "Tempo esgotado esperando o processamento do outbox.");
            await Task.Delay(100, Ct);
        }
    }

    private static async Task<int> CriarPedidoAsync(HttpClient cliente)
    {
        var sku = "O" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var criacaoDoProduto = await cliente.PostAsJsonAsync(
            "/api/produtos", new { nome = $"Produto outbox {sku}", sku, preco = 5m, custoUnitario = 1m, quantidade = 10 }, Ct);
        criacaoDoProduto.EnsureSuccessStatusCode();
        var produto = await criacaoDoProduto.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);

        var payload = new
        {
            clienteNome = "Cliente Outbox",
            clienteCpf = "52998224725",
            clienteEmail = "outbox@example.com",
            cep = "01001000",
            itens = new[] { new { produtoId = produto.Id, quantidade = 1 } },
        };
        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", payload, Ct);
        resposta.EnsureSuccessStatusCode();

        var pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);
        Assert.NotNull(pedido);
        return pedido.Id;
    }
}
