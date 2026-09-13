using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Estoque.IntegrationTests;

public sealed class LimiteDeRequisicoesTests(ApiFactory api)
{
    [Fact]
    public async Task Acima_do_limite_a_api_responde_429_com_retry_after_e_os_health_checks_continuam_livres()
    {
        var ct = TestContext.Current.CancellationToken;
        using var limitada = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LimiteDeRequisicoes:PermissoesPorJanela", "5");
            builder.UseSetting("LimiteDeRequisicoes:JanelaSegundos", "60");
            builder.UseSetting("Confirmacoes:Habilitado", "false");
        });
        using var cliente = limitada.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ChaveApi);

        var dentroDoLimite = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            dentroDoLimite.Add(await cliente.GetAsync("/api/produtos?tamanhoPagina=1", ct));
        }

        var acimaDoLimite = await cliente.GetAsync("/api/produtos?tamanhoPagina=1", ct);
        var healthChecks = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cliente.GetAsync("/health/live", ct)));

        Assert.All(dentroDoLimite, resposta => Assert.Equal(HttpStatusCode.OK, resposta.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, acimaDoLimite.StatusCode);
        Assert.NotNull(acimaDoLimite.Headers.RetryAfter);
        Assert.Equal("application/problem+json", acimaDoLimite.Content.Headers.ContentType?.MediaType);
        Assert.All(healthChecks, resposta => Assert.Equal(HttpStatusCode.OK, resposta.StatusCode));
    }

    // O limite vem antes da chave de API: quem tenta adivinhar a chave também é barrado.
    [Fact]
    public async Task Requisicoes_sem_chave_tambem_contam_para_o_limite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var limitada = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LimiteDeRequisicoes:PermissoesPorJanela", "3");
            builder.UseSetting("LimiteDeRequisicoes:JanelaSegundos", "60");
            builder.UseSetting("Confirmacoes:Habilitado", "false");
        });
        using var cliente = limitada.CreateClient();

        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            respostas.Add((await cliente.GetAsync("/api/produtos", ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], respostas);
    }
}
