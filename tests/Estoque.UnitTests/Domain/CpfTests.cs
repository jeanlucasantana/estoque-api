using Estoque.Api.Domain;

namespace Estoque.UnitTests.Domain;

public class CpfTests
{
    [Theory]
    [InlineData("52998224725")]
    [InlineData("11144477735")]
    public void Cpf_com_digitos_verificadores_corretos_e_valido(string cpf)
    {
        Assert.True(Cpf.EhValido(cpf));
    }

    [Theory]
    [InlineData("52998224724")]    // segundo dígito verificador errado
    [InlineData("52998224735")]    // primeiro dígito verificador errado
    [InlineData("11111111111")]    // todos os dígitos iguais
    [InlineData("5299822472")]     // 10 dígitos
    [InlineData("529982247250")]   // 12 dígitos
    [InlineData("529.982.247-25")] // com pontuação
    [InlineData("5299822472a")]
    [InlineData("")]
    [InlineData(null)]
    public void Cpf_invalido_e_rejeitado(string? cpf)
    {
        Assert.False(Cpf.EhValido(cpf));
    }

    [Fact]
    public void Final_retorna_os_dois_ultimos_digitos()
    {
        Assert.Equal("25", Cpf.Final("52998224725"));
    }
}
