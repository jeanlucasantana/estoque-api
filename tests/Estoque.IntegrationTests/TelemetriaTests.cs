using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Estoque.Api.Common;
using Estoque.Api.Features.Produtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Estoque.IntegrationTests;

public sealed class TelemetriaTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criar_pedido_e_recusar_por_estoque_registram_as_metricas_de_negocio()
    {
        var fabrica = api.Services.GetRequiredService<IMeterFactory>();
        using var criados = new MetricCollector<long>(fabrica, MetricasDeEstoque.NomeDoMeter, "estoque.pedidos.criados");
        using var recusados = new MetricCollector<long>(fabrica, MetricasDeEstoque.NomeDoMeter, "estoque.pedidos.recusados_por_estoque");
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 1);

        var aceito = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, "01001000"), Ct);
        var recusado = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, "01001000"), Ct);

        Assert.Equal(HttpStatusCode.Created, aceito.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, recusado.StatusCode);
        Assert.NotEmpty(criados.GetMeasurementSnapshot());
        Assert.NotEmpty(recusados.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task Falha_do_frete_registra_a_metrica_com_o_motivo()
    {
        var fabrica = api.Services.GetRequiredService<IMeterFactory>();
        using var falhas = new MetricCollector<long>(fabrica, MetricasDeEstoque.NomeDoMeter, "estoque.frete.falhas");
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, FreteFalso.CepComErro), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        Assert.Contains(falhas.GetMeasurementSnapshot(), medicao => medicao.Tags.TryGetValue("motivo", out var motivo) && Equals(motivo, "status_de_erro"));
    }

    private static object Payload(int produtoId, string cep) => new
    {
        clienteNome = "Cliente Telemetria",
        clienteCpf = "52998224725",
        clienteEmail = "telemetria@example.com",
        cep,
        itens = new[] { new { produtoId, quantidade = 1 } },
    };

    private static async Task<ProdutoResponse> CriarProdutoAsync(HttpClient cliente, int quantidade)
    {
        var sku = "T" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var resposta = await cliente.PostAsJsonAsync(
            "/api/produtos", new { nome = $"Produto telemetria {sku}", sku, preco = 5m, custoUnitario = 1m, quantidade }, Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }
}
