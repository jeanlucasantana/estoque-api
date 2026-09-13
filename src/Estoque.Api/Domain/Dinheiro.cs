namespace Estoque.Api.Domain;

public static class Dinheiro
{
    // Premissa: preço e custo de até 10 milhões. Com até 100 itens de até 1.000.000 unidades cada,
    // o subtotal de um pedido fica abaixo de 10^15 e cabe na coluna numeric(18,2).
    public const decimal ValorUnitarioMaximo = 10_000_000m;

    // RN04: arredondamento comercial, meio para cima e afastando de zero (2,345 -> 2,35; -2,345 -> -2,35).
    // O padrão do Math.Round é o arredondamento bancário, que daria 2,34.
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static bool TemNoMaximoDuasCasas(decimal valor) => Math.Round(valor, 2) == valor;
}
