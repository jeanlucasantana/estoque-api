using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Estoque.Api.Common;

namespace Estoque.Api.Features.Pedidos;

public sealed class FreteOptions
{
    public const string Secao = "Frete";

    [Required(ErrorMessage = "Frete:UrlBase não configurada.")]
    [Url(ErrorMessage = "Frete:UrlBase deve ser uma URL http ou https.")]
    public string UrlBase { get; set; } = string.Empty;
}

// Cliente tipado: o IHttpClientFactory reaproveita as conexões (sem esgotar portas, como o new HttpClient() do legado)
// e renova os handlers periodicamente (sem ficar preso a um DNS antigo, como um HttpClient estático).
public sealed class ServicoFrete(HttpClient http, MetricasDeEstoque metricas, ILogger<ServicoFrete> logger)
{
    // RN05. Sem retry: uma nova tentativa estouraria o limite de espera.
    public static readonly TimeSpan TempoMaximoDeEspera = TimeSpan.FromSeconds(2);

    // Premissa: um frete acima deste valor é tratado como resposta inválida do serviço, não como cobrança real.
    private const decimal FreteMaximo = 1_000_000m;

    // Retorna null quando o frete não pode ser obtido: falha, demora acima do limite ou resposta fora do contrato.
    // Quem chama responde 503; frete zero nunca é assumido.
    public async Task<decimal?> CalcularAsync(string cep, decimal valorPedido, CancellationToken cancellationToken)
    {
        // Ponto como separador decimal, qualquer que seja a cultura do servidor (o legado mandava "324,9" em pt-BR).
        var valor = valorPedido.ToString("0.00", CultureInfo.InvariantCulture);
        var rota = $"api/calcular?cep={Uri.EscapeDataString(cep)}&valor={valor}";
        var inicio = Stopwatch.GetTimestamp();

        try
        {
            using var resposta = await http.GetAsync(rota, cancellationToken);
            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning("Serviço de frete respondeu com status {StatusCode}", (int)resposta.StatusCode);
                return Falhou("status_de_erro");
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<RespostaFrete>(cancellationToken);
            if (corpo?.Valor is not { } frete || frete < 0 || frete > FreteMaximo)
            {
                logger.LogWarning("Serviço de frete respondeu sem um valor válido");
                return Falhou("resposta_invalida");
            }

            return frete;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelamento sem o cliente ter desistido da requisição: foi o timeout do HttpClient.
            logger.LogWarning("Serviço de frete não respondeu em {TempoMaximo}", TempoMaximoDeEspera);
            return Falhou("timeout");
        }
        catch (Exception erro) when (erro is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogWarning(erro, "Falha ao consultar o serviço de frete");
            return Falhou("erro_de_comunicacao");
        }
        finally
        {
            metricas.DuracaoDoFrete(Stopwatch.GetElapsedTime(inicio));
        }
    }

    private decimal? Falhou(string motivo)
    {
        metricas.FalhaDeFrete(motivo);
        return null;
    }

    private sealed record RespostaFrete(decimal? Valor);
}
