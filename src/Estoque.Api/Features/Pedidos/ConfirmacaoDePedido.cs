using System.Threading.Channels;
using Estoque.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Estoque.Api.Features.Pedidos;

// RN08. Fila em memória entre o endpoint (que só enfileira o id e responde) e o serviço em segundo plano (que envia).
// Limitação: confirmações ainda na fila se perdem se o processo reiniciar. Em produção, a solução é um outbox
// transacional, gravando a confirmação pendente na mesma transação do pedido (ver README).
public sealed class FilaDeConfirmacoes
{
    private readonly Channel<int> _canal = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public void Enfileirar(int pedidoId) => _canal.Writer.TryWrite(pedidoId);

    public IAsyncEnumerable<int> LerAsync(CancellationToken cancellationToken) => _canal.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ServicoDeConfirmacao(
    FilaDeConfirmacoes fila,
    IServiceScopeFactory escopos,
    ILogger<ServicoDeConfirmacao> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pedidoId in fila.LerAsync(stoppingToken))
        {
            try
            {
                await EnviarAsync(pedidoId, stoppingToken);
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                // Uma confirmação com falha não pode derrubar o serviço: uma exceção que escapa de um BackgroundService
                // encerra a aplicação inteira.
                logger.LogError(erro, "Falha ao enviar a confirmação do pedido {PedidoId}", pedidoId);
            }
        }
    }

    private async Task EnviarAsync(int pedidoId, CancellationToken cancellationToken)
    {
        // O serviço é Singleton e o DbContext é Scoped: injetar o DbContext direto seria uma dependência cativa.
        // Cada envio cria o próprio escopo.
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = await db.Pedidos
            .Where(p => p.Id == pedidoId)
            .Select(p => p.ClienteEmail)
            .FirstOrDefaultAsync(cancellationToken);

        if (email is null)
        {
            logger.LogWarning("Pedido {PedidoId} não encontrado para confirmação", pedidoId);
            return;
        }

        // O "envio" deste desafio é só o log. O email é usado no envio, mas nunca registrado (RN07).
        logger.LogInformation("Confirmação do pedido {PedidoId} enviada ao cliente", pedidoId);
    }
}
