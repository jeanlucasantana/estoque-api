using System.ComponentModel.DataAnnotations;
using Estoque.Api.Common;
using Estoque.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Estoque.Api.Features.Pedidos;

public sealed class ConfirmacoesOptions
{
    public const string Secao = "Confirmacoes";

    [Range(1, 300, ErrorMessage = "Confirmacoes:IntervaloSegundos deve estar entre 1 e 300.")]
    public int IntervaloSegundos { get; set; } = 2;

    [Range(1, 500, ErrorMessage = "Confirmacoes:TamanhoDoLote deve estar entre 1 e 500.")]
    public int TamanhoDoLote { get; set; } = 20;

    // Permite subir uma instância que só atende requisições, sem processar confirmações.
    public bool Habilitado { get; set; } = true;
}

// RN08 com outbox transacional: a confirmação pendente é gravada na mesma transação do pedido.
// Se o pedido foi gravado, a confirmação também foi, e um reinício não perde nada, porque ela está no banco.
public sealed class ConfirmacaoPendente(int pedidoId, DateTimeOffset criadaEm)
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(5);

    public long Id { get; private set; }

    public int PedidoId { get; private set; } = pedidoId;

    public DateTimeOffset CriadaEm { get; private set; } = criadaEm;

    public int Tentativas { get; private set; }

    public DateTimeOffset ProximaTentativaEm { get; private set; } = criadaEm;

    public DateTimeOffset? EnviadaEm { get; private set; }

    public void MarcarComoEnviada(DateTimeOffset agora) => EnviadaEm = agora;

    // Espera crescente entre tentativas (2, 4, 8... segundos, até 5 minutos), para não insistir num provedor fora do ar.
    public void RegistrarFalha(DateTimeOffset agora)
    {
        Tentativas++;
        var espera = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(Tentativas, 16)));
        ProximaTentativaEm = agora + (espera < EsperaMaxima ? espera : EsperaMaxima);
    }
}

// Lê o outbox em lotes. É Singleton; cada lote usa um escopo e uma transação próprios, com o seu DbContext.
public sealed class ProcessadorDeConfirmacoes(
    IServiceScopeFactory escopos,
    IOptions<ConfirmacoesOptions> options,
    TimeProvider relogio,
    MetricasDeEstoque metricas,
    ILogger<ProcessadorDeConfirmacoes> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opcoes = options.Value;
        if (!opcoes.Habilitado)
        {
            logger.LogWarning("Processamento de confirmações desabilitado nesta instância");
            return;
        }

        using var intervalo = new PeriodicTimer(TimeSpan.FromSeconds(opcoes.IntervaloSegundos));
        do
        {
            try
            {
                // Esvazia o que estiver pendente antes de esperar o próximo intervalo.
                int processadas;
                do
                {
                    processadas = await ProcessarLoteAsync(opcoes.TamanhoDoLote, stoppingToken);
                }
                while (processadas == opcoes.TamanhoDoLote);
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                // Uma exceção que escapa de um BackgroundService encerra a aplicação inteira.
                logger.LogError(erro, "Falha ao processar um lote de confirmações");
            }
        }
        while (await intervalo.WaitForNextTickAsync(stoppingToken));
    }

    // FOR UPDATE SKIP LOCKED: cada instância trava as linhas que pegou, e as outras pulam essas linhas em vez de esperar.
    // Várias réplicas processam em paralelo sem enviar a mesma confirmação duas vezes.
    private async Task<int> ProcessarLoteAsync(int tamanhoDoLote, CancellationToken cancellationToken)
    {
        // A busca roda a cada intervalo e quase sempre volta vazia: sem suprimir, cada busca viraria um trace.
        // Só um lote com confirmações gera o span "confirmacoes.processar_lote", com os comandos do banco dentro.
        using var semTelemetria = SuppressInstrumentationScope.Begin();

        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var agora = relogio.GetUtcNow();

        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        var lote = await db.ConfirmacoesPendentes
            .FromSql($"""
                SELECT * FROM confirmacoes_pendentes
                WHERE enviada_em IS NULL AND proxima_tentativa_em <= {agora}
                ORDER BY id
                LIMIT {tamanhoDoLote}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (lote.Count == 0)
        {
            return 0;
        }

        using var comTelemetria = SuppressInstrumentationScope.Begin(false);
        using var atividade = Telemetria.Atividades.StartActivity("confirmacoes.processar_lote");
        atividade?.SetTag("confirmacoes.quantidade", lote.Count);

        foreach (var confirmacao in lote)
        {
            try
            {
                Enviar(confirmacao);
                confirmacao.MarcarComoEnviada(relogio.GetUtcNow());
                metricas.ConfirmacaoEnviada();
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                confirmacao.RegistrarFalha(relogio.GetUtcNow());
                logger.LogWarning(
                    erro,
                    "Falha ao enviar a confirmação do pedido {PedidoId} (tentativa {Tentativa})",
                    confirmacao.PedidoId,
                    confirmacao.Tentativas);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return lote.Count;
    }

    // O "envio" deste desafio é só o log. Um envio real buscaria o email do pedido e o passaria ao provedor com o Id da
    // confirmação como chave de deduplicação: se o processo cair depois de enviar e antes do commit, a confirmação é
    // enviada de novo (entrega "pelo menos uma vez"). O email nunca é registrado em log (RN07).
    private void Enviar(ConfirmacaoPendente confirmacao) =>
        logger.LogInformation("Confirmação do pedido {PedidoId} enviada ao cliente", confirmacao.PedidoId);
}
