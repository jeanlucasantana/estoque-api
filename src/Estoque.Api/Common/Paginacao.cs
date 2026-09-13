using System.ComponentModel.DataAnnotations;

namespace Estoque.Api.Common;

// Record com valores padrão no construtor: com [AsParameters], é assim que os parâmetros viram opcionais na query string.
// O teto de pagina evita estouro de inteiro no deslocamento (pagina - 1) * tamanhoPagina.
public sealed record ParametrosPaginacao(
    [Range(1, 1_000_000, ErrorMessage = "pagina deve estar entre 1 e 1000000.")] int Pagina = 1,
    [Range(1, 100, ErrorMessage = "tamanhoPagina deve estar entre 1 e 100.")] int TamanhoPagina = 20)
{
    public int Deslocamento => (Pagina - 1) * TamanhoPagina;
}

public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int Total);
