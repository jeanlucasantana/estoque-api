using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Estoque.IntegrationTests;

public sealed class AutenticacaoESaudeTests(ApiFactory api)
{
    [Fact]
    public async Task Rota_da_api_sem_chave_retorna_401_em_problem_details()
    {
        using var cliente = api.CreateClient();

        var resposta = await cliente.GetAsync("/api/produtos", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Chave_errada_retorna_401()
    {
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", "chave-errada");

        var resposta = await cliente.GetAsync("/api/produtos", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_checks_respondem_200_sem_chave(string rota)
    {
        using var cliente = api.CreateClient();

        var resposta = await cliente.GetAsync(rota, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Resposta_de_erro_traz_traceId_para_correlacionar_com_os_logs()
    {
        using var cliente = api.CriarClienteAutenticado();

        var resposta = await cliente.GetAsync("/api/rota-inexistente", TestContext.Current.CancellationToken);
        var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.True(problema.TryGetProperty("traceId", out _));
    }

    [Fact]
    public void Aplicacao_nao_sobe_sem_chave_de_api_configurada()
    {
        using var semChave = api.WithWebHostBuilder(builder => builder.UseSetting("Api:Chave", string.Empty));

        var erro = Assert.ThrowsAny<Exception>(() => semChave.CreateClient());

        Assert.Contains("Api:Chave", erro.ToString());
    }
}
