using Estoque.Api.Domain;

namespace Estoque.UnitTests.Domain;

public class CalculadoraPedidoTests
{
    // Sexta-feira, 11/09/2026, 12h em Brasília: o instante do exemplo do contrato.
    private static readonly DateTimeOffset Sexta = new(2026, 9, 11, 15, 0, 0, TimeSpan.Zero);

    // Quarta-feira, 09/09/2026, 12h em Brasília.
    private static readonly DateTimeOffset Quarta = new(2026, 9, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Subtotal_soma_preco_unitario_vezes_quantidade_de_todos_os_itens()
    {
        var subtotal = CalculadoraPedido.CalcularSubtotal([new ItemCalculo(0.35m, 100), new ItemCalculo(289.90m, 1)]);

        Assert.Equal(324.90m, subtotal);
    }

    [Fact]
    public void Exemplo_do_contrato_na_sexta_aplica_10_por_cento_somente_sobre_o_subtotal()
    {
        var valores = CalculadoraPedido.Calcular(subtotal: 324.90m, frete: 25.90m, criadoEm: Sexta);

        Assert.Equal(new ValoresPedido(Subtotal: 324.90m, Desconto: 32.49m, Frete: 25.90m, Total: 318.31m), valores);
    }

    [Fact]
    public void Fora_da_sexta_nao_ha_desconto()
    {
        var valores = CalculadoraPedido.Calcular(subtotal: 324.90m, frete: 25.90m, criadoEm: Quarta);

        Assert.Equal(new ValoresPedido(Subtotal: 324.90m, Desconto: 0m, Frete: 25.90m, Total: 350.80m), valores);
    }

    [Fact]
    public void Desconto_com_meio_centavo_arredonda_para_cima()
    {
        // 10% de 0,25 = 0,025, que vira 0,03 (o arredondamento bancário daria 0,02).
        var valores = CalculadoraPedido.Calcular(subtotal: 0.25m, frete: 0m, criadoEm: Sexta);

        Assert.Equal(0.03m, valores.Desconto);
        Assert.Equal(0.22m, valores.Total);
    }
}
