using System.ComponentModel.DataAnnotations;
using Estoque.Api.Domain;

namespace Estoque.Api.Features.Produtos;

// Contrato de entrada separado da entidade: o cliente não consegue definir Id nem Ativo (overposting do legado).
public sealed class SalvarProdutoRequest : IValidatableObject
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MaxLength(120, ErrorMessage = "O nome deve ter no máximo 120 caracteres.")]
    public required string Nome { get; init; }

    [Required(ErrorMessage = "O SKU é obrigatório.")]
    [MaxLength(30, ErrorMessage = "O SKU deve ter no máximo 30 caracteres.")]
    [RegularExpression("^[A-Z0-9]+$", ErrorMessage = "O SKU deve conter apenas letras maiúsculas e dígitos.")]
    public required string Sku { get; init; }

    public required decimal Preco { get; init; }

    public required decimal CustoUnitario { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "A quantidade em estoque deve ser maior ou igual a zero.")]
    public required int Quantidade { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Preco <= 0 || !Dinheiro.TemNoMaximoDuasCasas(Preco))
        {
            yield return new ValidationResult(
                "O preço deve ser maior que zero e ter no máximo duas casas decimais.", [nameof(Preco)]);
        }

        if (CustoUnitario < 0 || !Dinheiro.TemNoMaximoDuasCasas(CustoUnitario))
        {
            yield return new ValidationResult(
                "O custo unitário deve ser maior ou igual a zero e ter no máximo duas casas decimais.", [nameof(CustoUnitario)]);
        }
    }
}

// Sem CustoUnitario: é informação comercial interna (RN01).
public sealed record ProdutoResponse(int Id, string Nome, string Sku, decimal Preco, int Quantidade, bool Ativo)
{
    public static ProdutoResponse De(Produto produto) =>
        new(produto.Id, produto.Nome, produto.Sku, produto.Preco, produto.Quantidade, produto.Ativo);
}
