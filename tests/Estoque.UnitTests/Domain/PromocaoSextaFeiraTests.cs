using System.Globalization;
using Estoque.Api.Domain;

namespace Estoque.UnitTests.Domain;

// Brasília está em UTC-3: perto da meia-noite, o dia da semana em UTC é diferente do dia em Brasília.
// Os casos cobrem as duas bordas da sexta-feira, que é onde um cálculo feito em UTC erraria.
public class PromocaoSextaFeiraTests
{
    [Theory]
    [InlineData("2026-09-11T02:59:59Z", false)]      // quinta 23:59:59 em Brasília (em UTC já é sexta)
    [InlineData("2026-09-11T03:00:00Z", true)]       // sexta 00:00:00 em Brasília
    [InlineData("2026-09-12T02:59:59Z", true)]       // sexta 23:59:59 em Brasília (em UTC já é sábado)
    [InlineData("2026-09-12T03:00:00Z", false)]      // sábado 00:00:00 em Brasília
    [InlineData("2026-09-11T05:00:00+02:00", true)]  // sexta 00:00:00 em Brasília, escrito com outro fuso
    public void Sexta_feira_e_decidida_pelo_horario_de_Brasilia(string instante, bool esperado)
    {
        var data = DateTimeOffset.Parse(instante, CultureInfo.InvariantCulture);

        var resultado = CalculadoraPedido.EhSextaFeiraEmBrasilia(data);

        Assert.Equal(esperado, resultado);
    }
}
