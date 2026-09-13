namespace Estoque.Api.Common;

// Os endpoints recebem pagina e tamanhoPagina como parâmetros comuns, validados com [Range] e estas constantes.
// Com [AsParameters] e atributos de validação, a geração do documento OpenAPI do .NET 10 falha com InvalidCastException.
public sealed record ParametrosPaginacao(int Pagina, int TamanhoPagina)
{
    // O teto de pagina evita estouro de inteiro no deslocamento (pagina - 1) * tamanhoPagina.
    public const int PaginaMaxima = 1_000_000;
    public const int TamanhoMaximo = 100;
    public const string MensagemPagina = "pagina deve estar entre 1 e 1000000.";
    public const string MensagemTamanho = "tamanhoPagina deve estar entre 1 e 100.";

    private const int TamanhoPadrao = 20;

    public int Deslocamento => (Pagina - 1) * TamanhoPagina;

    public static ParametrosPaginacao De(int? pagina, int? tamanhoPagina) => new(pagina ?? 1, tamanhoPagina ?? TamanhoPadrao);
}

public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int Total);
