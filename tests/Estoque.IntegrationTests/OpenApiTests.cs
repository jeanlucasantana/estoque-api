using System.Net;

namespace Estoque.IntegrationTests;

// O WebApplicationFactory sobe a API em Development, onde o documento OpenAPI é publicado.
public sealed class OpenApiTests(ApiFactory api)
{
    [Fact]
    public async Task Documento_openapi_e_gerado_com_as_rotas_da_api()
    {
        using var cliente = api.CreateClient();

        var resposta = await cliente.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var documento = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains("/api/produtos", documento);
        Assert.Contains("/api/pedidos/{id}/cancelamento", documento);
    }
}
