namespace Estoque.Api.Domain;

public static class Dinheiro
{
    // RN04: arredondamento comercial, meio para cima e afastando de zero (2,345 -> 2,35; -2,345 -> -2,35).
    // O padrão do Math.Round é o arredondamento bancário, que daria 2,34.
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static bool TemNoMaximoDuasCasas(decimal valor) => Math.Round(valor, 2) == valor;
}
