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
        logger.LogError(exception, "Erro inesperado em {Metodo} {Rota}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro inesperado.",
                Detail = "Informe o traceId ao suporte para localizar a falha.",
            },
        });
    }
}
