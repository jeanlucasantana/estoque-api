using System.Diagnostics;
using System.Diagnostics.Metrics;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Estoque.Api.Common;

// Métricas de negócio. O Meter vem do IMeterFactory (injeção), e não de um campo estático, o que isola as medições
// de cada host (inclusive nos testes).
public sealed class MetricasDeEstoque
{
    public const string NomeDoMeter = "Estoque.Api";

    private readonly Counter<long> _pedidosCriados;
    private readonly Counter<long> _pedidosRecusadosPorEstoque;
    private readonly Counter<long> _falhasDeFrete;
    private readonly Histogram<double> _duracaoDoFrete;
    private readonly Counter<long> _confirmacoesEnviadas;

    public MetricasDeEstoque(IMeterFactory fabrica)
    {
        var meter = fabrica.Create(NomeDoMeter);
        _pedidosCriados = meter.CreateCounter<long>("estoque.pedidos.criados", "{pedido}", "Pedidos criados com sucesso");
        _pedidosRecusadosPorEstoque = meter.CreateCounter<long>(
            "estoque.pedidos.recusados_por_estoque", "{pedido}", "Pedidos recusados por estoque insuficiente");
        _falhasDeFrete = meter.CreateCounter<long>("estoque.frete.falhas", "{falha}", "Consultas de frete que falharam, por motivo");
        _duracaoDoFrete = meter.CreateHistogram<double>("estoque.frete.duracao", "s", "Duração das consultas ao serviço de frete");
        _confirmacoesEnviadas = meter.CreateCounter<long>("estoque.confirmacoes.enviadas", "{confirmacao}", "Confirmações de pedido enviadas");
    }

    public void PedidoCriado() => _pedidosCriados.Add(1);

    public void PedidoRecusadoPorEstoque() => _pedidosRecusadosPorEstoque.Add(1);

    public void FalhaDeFrete(string motivo) => _falhasDeFrete.Add(1, new KeyValuePair<string, object?>("motivo", motivo));

    public void DuracaoDoFrete(TimeSpan duracao) => _duracaoDoFrete.Record(duracao.TotalSeconds);

    public void ConfirmacaoEnviada() => _confirmacoesEnviadas.Add(1);
}

public static class Telemetria
{
    public const string NomeDoServico = "estoque-api";

    // Spans próprios da aplicação, para o trabalho que não nasce de uma requisição HTTP (o processador do outbox).
    public static readonly ActivitySource Atividades = new("Estoque.Api");

    // Traces: requisições HTTP recebidas, chamadas HTTP feitas (frete), comandos no PostgreSQL e spans próprios.
    // Métricas: as de negócio acima, as do ASP.NET Core, do HttpClient, do Npgsql e do runtime do .NET.
    // A exportação por OTLP só é ligada quando há um coletor configurado em OTEL_EXPORTER_OTLP_ENDPOINT.
    public static IServiceCollection AddTelemetria(this IServiceCollection services, IConfiguration configuracao)
    {
        services.AddSingleton<MetricasDeEstoque>();

        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(NomeDoServico))
            .WithTracing(tracing => tracing
                .AddSource(Atividades.Name)
                .AddAspNetCoreInstrumentation(opcoes =>
                    opcoes.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metricas => metricas
                .AddMeter(MetricasDeEstoque.NomeDoMeter)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("Npgsql")
                .AddMeter("System.Runtime"))
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(configuracao["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        return services;
    }
}
