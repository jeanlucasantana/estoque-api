using Microsoft.AspNetCore.Diagnostics;

namespace Estoque.Api.Common;

// Última barreira: qualquer exceção não tratada vira um 500 genérico. O detalhe fica só no log,
// e o traceId que o ProblemDetails inclui na resposta é o mesmo que aparece no escopo do log.
public sealed class TratadorDeExcecoesInesperadas(
    IProblemDetailsService problemDetails,
    ILogger<TratadorDeExcecoesInesperadas> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Parâmetro obrigatório ausente, JSON malformado etc. Em Development o ASP.NET Core lança esta exceção
        // em vez de só responder 400; é erro do cliente e mantém o status que ela carrega.
        if (exception is BadHttpRequestException requisicaoInvalida)
        {
            return await EscreverAsync(httpContext, requisicaoInvalida.StatusCode, "Requisição inválida.", detalhe: null);
        }

        logger.LogError(exception, "Erro inesperado em {Metodo} {Rota}", httpContext.Request.Method, httpContext.Request.Path);

        return await EscreverAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "Erro inesperado.",
            "Informe o traceId ao suporte para localizar a falha.");
    }

    private ValueTask<bool> EscreverAsync(HttpContext httpContext, int status, string titulo, string? detalhe)
    {
        httpContext.Response.StatusCode = status;
        return problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status, Title = titulo, Detail = detalhe },
        });
    }
}
