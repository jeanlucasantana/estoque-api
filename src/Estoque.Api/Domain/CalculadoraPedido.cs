namespace Estoque.Api.Domain;

public sealed record ItemCalculo(decimal PrecoUnitario, int Quantidade);

public sealed record ValoresPedido(decimal Subtotal, decimal Desconto, decimal Frete, decimal Total);

public static class CalculadoraPedido
{
    private const decimal PercentualDescontoSexta = 0.10m;

    // Id IANA: no Linux vem do tzdata (na imagem chiseled, só a variante "extra" o inclui); no Windows o .NET converte via ICU.
    private static readonly TimeZoneInfo FusoBrasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static decimal CalcularSubtotal(IEnumerable<ItemCalculo> itens) =>
        Dinheiro.Arredondar(itens.Sum(item => item.PrecoUnitario * item.Quantidade));

    public static ValoresPedido Calcular(decimal subtotal, decimal frete, DateTimeOffset criadoEm)
    {
        var desconto = EhSextaFeiraEmBrasilia(criadoEm)
            ? Dinheiro.Arredondar(subtotal * PercentualDescontoSexta)
            : 0m;

        // O frete nunca recebe desconto (RN04).
        var freteArredondado = Dinheiro.Arredondar(frete);

        return new ValoresPedido(subtotal, desconto, freteArredondado, subtotal - desconto + freteArredondado);
    }

    public static bool EhSextaFeiraEmBrasilia(DateTimeOffset instante) =>
        TimeZoneInfo.ConvertTime(instante, FusoBrasilia).DayOfWeek == DayOfWeek.Friday;
}
