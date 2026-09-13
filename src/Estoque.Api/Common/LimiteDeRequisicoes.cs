using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Estoque.Api.Common;

public sealed class LimiteDeRequisicoesOptions
{
    public const string Secao = "LimiteDeRequisicoes";

    [Range(1, 1_000_000, ErrorMessage = "LimiteDeRequisicoes:PermissoesPorJanela deve estar entre 1 e 1000000.")]
    public int PermissoesPorJanela { get; set; } = 300;

    [Range(1, 3600, ErrorMessage = "LimiteDeRequisicoes:JanelaSegundos deve estar entre 1 e 3600.")]
    public int JanelaSegundos { get; set; } = 10;
}

public static class LimiteDeRequisicoes
{
    // Janela fixa por endereço IP, só nas rotas /api (health checks ficam livres para o orquestrador).
    // Atrás de um proxy ou load balancer, o IP real do cliente precisa vir do X-Forwarded-For (ForwardedHeaders);
    // sem isso, todos os clientes cairiam na mesma partição.
    public static IServiceCollection AddLimiteDeRequisicoes(this IServiceCollection services)
    {
        services.AddOptions<LimiteDeRequisicoesOptions>()
            .BindConfiguration(LimiteDeRequisicoesOptions.Secao)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!http.Request.Path.StartsWithSegments("/api"))
                {
                    return RateLimitPartition.GetNoLimiter("fora-da-api");
                }

                var limites = http.RequestServices.GetRequiredService<IOptions<LimiteDeRequisicoesOptions>>().Value;
                var cliente = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                return RateLimitPartition.GetFixedWindowLimiter(cliente, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limites.PermissoesPorJanela,
                    Window = TimeSpan.FromSeconds(limites.JanelaSegundos),
                    QueueLimit = 0,
                });
            });

            opcoes.OnRejected = async (contexto, cancellationToken) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(espera.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = contexto.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = contexto.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Muitas requisições.",
                        Detail = "O limite de requisições foi atingido. Aguarde o tempo indicado em Retry-After.",
                    },
                });
            };
        });

        return services;
    }
}
