using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Estoque.Api.Common;

// Middleware, e não filtro de endpoint, para responder 401 antes de qualquer binding ou validação:
// uma requisição sem chave e com corpo inválido recebe 401, não 400. Também cobre rotas inexistentes sob /api.
public sealed class ChaveApiMiddleware(RequestDelegate next, IOptions<ApiOptions> options, IProblemDetailsService problemDetails)
{
    public const string Cabecalho = "X-Api-Key";

    private readonly byte[] _hashEsperado = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Chave));

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") && !ChaveValida(context.Request.Headers[Cabecalho].ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await problemDetails.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = StatusCodes.Status401Unauthorized, Title = "Chave de API ausente ou inválida." },
            });
            return;
        }

        await next(context);
    }

    // Compara hashes de tamanho fixo: FixedTimeEquals leva o mesmo tempo acerte ou erre,
    // então o tempo de resposta não revela quantos caracteres da chave estão corretos.
    private bool ChaveValida(string chaveRecebida)
    {
        var hashRecebido = SHA256.HashData(Encoding.UTF8.GetBytes(chaveRecebida));
        return CryptographicOperations.FixedTimeEquals(hashRecebido, _hashEsperado);
    }
}
