using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Estoque.Api.Features.Pedidos;

// Registro de uma Idempotency-Key usada numa criação bem-sucedida. A chave é a chave primária da tabela: duas
// requisições simultâneas com a mesma chave não conseguem gravar as duas, porque o banco recusa a segunda.
public sealed class ChaveDeIdempotencia(string chave, string hashDaRequisicao, int pedidoId, DateTimeOffset criadaEm)
{
    public string Chave { get; private set; } = chave;

    // SHA-256 do corpo da requisição: detecta a mesma chave reaproveitada para um pedido diferente.
    public string HashDaRequisicao { get; private set; } = hashDaRequisicao;

    public int PedidoId { get; private set; } = pedidoId;

    public DateTimeOffset CriadaEm { get; private set; } = criadaEm;
}

public static class Idempotencia
{
    public const string Cabecalho = "Idempotency-Key";
    public const string CabecalhoDeRepeticao = "Idempotent-Replayed";
    public const int TamanhoMaximoDaChave = 100;
    public const string MensagemTamanho = "Idempotency-Key deve ter entre 1 e 100 caracteres.";

    public static string CalcularHash(CriarPedidoRequest request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    public static bool EhChaveRepetida(DbUpdateException erro) =>
        erro.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pk_chaves_idempotencia" };
}
