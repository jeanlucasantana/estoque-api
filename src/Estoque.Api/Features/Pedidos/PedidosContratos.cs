using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Estoque.Api.Domain;

namespace Estoque.Api.Features.Pedidos;

public sealed class CriarPedidoRequest : IValidatableObject
{
    // Premissas: até 100 itens e até 1.000.000 unidades por item. Limita o tamanho da requisição
    // e impede estouro de inteiro ao consolidar itens repetidos.
    private const int MaximoDeItens = 100;
    private const int QuantidadeMaximaPorItem = 1_000_000;

    [Required(ErrorMessage = "O nome do cliente é obrigatório.")]
    [MaxLength(200, ErrorMessage = "O nome do cliente deve ter no máximo 200 caracteres.")]
    public required string ClienteNome { get; init; }

    [Required(ErrorMessage = "O CPF do cliente é obrigatório.")]
    public required string ClienteCpf { get; init; }

    [Required(ErrorMessage = "O email do cliente é obrigatório.")]
    [MaxLength(254, ErrorMessage = "O email do cliente deve ter no máximo 254 caracteres.")]
    public required string ClienteEmail { get; init; }

    [Required(ErrorMessage = "O CEP é obrigatório.")]
    [RegularExpression("^[0-9]{8}$", ErrorMessage = "O CEP deve ter 8 dígitos numéricos.")]
    public required string Cep { get; init; }

    public required IReadOnlyList<ItemPedidoRequest> Itens { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Cpf.EhValido(ClienteCpf))
        {
            yield return new ValidationResult("O CPF do cliente é inválido.", [nameof(ClienteCpf)]);
        }

        if (!EmailValido(ClienteEmail))
        {
            yield return new ValidationResult("O email do cliente é inválido.", [nameof(ClienteEmail)]);
        }

        if (Itens is not { Count: > 0 and <= MaximoDeItens })
        {
            yield return new ValidationResult($"O pedido deve ter entre 1 e {MaximoDeItens} itens.", [nameof(Itens)]);
            yield break;
        }

        for (var i = 0; i < Itens.Count; i++)
        {
            if (Itens[i].Quantidade is <= 0 or > QuantidadeMaximaPorItem)
            {
                yield return new ValidationResult(
                    $"A quantidade de cada item deve estar entre 1 e {QuantidadeMaximaPorItem}.",
                    [$"{nameof(Itens)}[{i}].{nameof(ItemPedidoRequest.Quantidade)}"]);
            }
        }
    }

    private static bool EmailValido(string? email) =>
        MailAddress.TryCreate(email, out var endereco)
        && endereco.Address == email
        && endereco.Host.Contains('.');
}

public sealed record ItemPedidoRequest(int ProdutoId, int Quantidade);

public sealed record ItemPedidoResponse(int ProdutoId, string Sku, int Quantidade, decimal PrecoUnitario);

public sealed record ClienteResponse(string Nome, string CpfFinal);

// Detalhe (RN07): o CPF aparece só com os dois últimos dígitos, e o email não aparece.
// CriadoEm em DateTime UTC para serializar com "Z", como no contrato.
public sealed record PedidoResponse(
    int Id,
    StatusPedido Status,
    DateTime CriadoEm,
    IReadOnlyList<ItemPedidoResponse> Itens,
    decimal Subtotal,
    decimal Desconto,
    decimal Frete,
    decimal Total,
    ClienteResponse Cliente)
{
    public static PedidoResponse De(Pedido pedido) => new(
        pedido.Id,
        pedido.Status,
        pedido.CriadoEm.UtcDateTime,
        [.. pedido.Itens.OrderBy(i => i.ProdutoId).Select(i => new ItemPedidoResponse(i.ProdutoId, i.Sku, i.Quantidade, i.PrecoUnitario))],
        pedido.Subtotal,
        pedido.Desconto,
        pedido.Frete,
        pedido.Total,
        new ClienteResponse(pedido.ClienteNome, Cpf.Final(pedido.ClienteCpf)));
}

// Listagem (RN07): sem CPF e sem email.
public sealed record PedidoResumoResponse(
    int Id,
    StatusPedido Status,
    DateTime CriadoEm,
    string ClienteNome,
    int QuantidadeItens,
    decimal Total);
