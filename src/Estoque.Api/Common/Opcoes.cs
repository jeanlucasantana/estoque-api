using System.ComponentModel.DataAnnotations;

namespace Estoque.Api.Common;

public sealed class ApiOptions
{
    public const string Secao = "Api";

    // A chave do legado é considerada comprometida. O tamanho mínimo impede que ela seja trocada por outra trivial.
    [Required(ErrorMessage = "Api:Chave não configurada.")]
    [MinLength(32, ErrorMessage = "Api:Chave deve ter pelo menos 32 caracteres.")]
    public string Chave { get; set; } = string.Empty;
}

public sealed class BancoDeDadosOptions
{
    [Required(ErrorMessage = "ConnectionStrings:Estoque não configurada.")]
    public string ConnectionString { get; set; } = string.Empty;
}
