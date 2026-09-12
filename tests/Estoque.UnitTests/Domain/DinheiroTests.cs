using Estoque.Api.Domain;

namespace Estoque.UnitTests.Domain;

public class DinheiroTests
{
    public static TheoryData<decimal, decimal> CasosDeArredondamento => new()
    {
        { 2.345m, 2.35m },   // o arredondamento bancário (padrão do .NET) daria 2,34
        { 2.365m, 2.37m },   // o bancário daria 2,36
        { -2.345m, -2.35m }, // afasta de zero
        { 2.344m, 2.34m },
        { 32.49m, 32.49m },
    };

    [Theory]
    [MemberData(nameof(CasosDeArredondamento))]
    public void Arredondar_usa_arredondamento_comercial(decimal valor, decimal esperado)
    {
        Assert.Equal(esperado, Dinheiro.Arredondar(valor));
    }

    [Theory]
    [InlineData(0.35, true)]
    [InlineData(289.9, true)]
    [InlineData(10, true)]
    [InlineData(0.355, false)]
    public void TemNoMaximoDuasCasas_identifica_valores_com_mais_de_duas_casas(double valor, bool esperado)
    {
        Assert.Equal(esperado, Dinheiro.TemNoMaximoDuasCasas((decimal)valor));
    }
}
