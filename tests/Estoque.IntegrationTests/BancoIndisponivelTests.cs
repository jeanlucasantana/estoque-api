using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Estoque.IntegrationTests;

public sealed class BancoIndisponivelTests(ApiFactory api)
{
    [Fact]
    public async Task Com_o_banco_inacessivel_live_responde_200_e_ready_e_a_api_respondem_503()
    {
        // A porta 1 recusa a conexão: simula o banco fora do ar sem derrubar o PostgreSQL compartilhado pelos outros testes.
        using var semBanco = api.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:Estoque",
            "Host=127.0.0.1;Port=1;Database=estoque;Username=estoque;Password=indisponivel;Timeout=3"));
        using var cliente = semBanco.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ChaveApi);
        var ct = TestContext.Current.CancellationToken;

        var live = await cliente.GetAsync("/health/live", ct);
        var ready = await cliente.GetAsync("/health/ready", ct);
        var produtos = await cliente.GetAsync("/api/produtos", ct);
        var problema = await produtos.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, produtos.StatusCode);
        Assert.True(problema.TryGetProperty("traceId", out _));
    }
}
