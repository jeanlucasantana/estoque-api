using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Estoque.IntegrationTests;

// Substitui só a rede: o ServicoFrete e o HttpClient reais rodam, com o timeout real de 2 segundos.
// Reproduz o contrato e os CEPs especiais do simulador WireMock do enunciado, sem depender de serviço externo.
public sealed partial class FreteFalso : HttpMessageHandler
{
    public const string CepLento = "99999000";
    public const string CepComErro = "99999999";
    public const string CepComRespostaInvalida = "99999998";
    public const string CepComFreteAbsurdo = "99999997";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var query = QueryHelpers.ParseQuery(request.RequestUri?.Query);
        var cep = query["cep"].ToString();
        var valor = query["valor"].ToString();

        // Como o simulador: requisição fora do formato recebe 404 (inclusive valor com vírgula).
        if (request.RequestUri?.AbsolutePath != "/api/calcular" || !FormatoCep().IsMatch(cep) || !FormatoValor().IsMatch(valor))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        switch (cep)
        {
            case CepLento:
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return Json(HttpStatusCode.OK, """{ "valor": 25.90 }""");
            case CepComErro:
                return Json(HttpStatusCode.InternalServerError, """{ "erro": "servico indisponivel" }""");
            case CepComRespostaInvalida:
                return Json(HttpStatusCode.OK, """{ "erro": "sem valor" }""");
            case CepComFreteAbsurdo:
                return Json(HttpStatusCode.OK, """{ "valor": 1000000.01 }""");
            default:
                return Json(HttpStatusCode.OK, """{ "valor": 25.90 }""");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string corpo) =>
        new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex FormatoCep();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]{1,2})?$")]
    private static partial Regex FormatoValor();
}
