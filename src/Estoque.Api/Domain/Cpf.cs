namespace Estoque.Api.Domain;

public static class Cpf
{
    // Premissa: o CPF chega só com os 11 dígitos, sem pontuação, como no exemplo do contrato.
    public static bool EhValido(string? cpf)
    {
        if (cpf is null || cpf.Length != 11 || !cpf.All(char.IsAsciiDigit))
        {
            return false;
        }

        // 000.000.000-00, 111.111.111-11 etc. passam no cálculo dos dígitos, mas não são CPFs válidos.
        if (cpf.All(digito => digito == cpf[0]))
        {
            return false;
        }

        return DigitoVerificador(cpf[..9]) == cpf[9] - '0'
            && DigitoVerificador(cpf[..10]) == cpf[10] - '0';
    }

    public static string Final(string cpf) => cpf[^2..];

    // Pesos decrescentes a partir de (quantidade de dígitos + 1): 10..2 para o primeiro dígito, 11..2 para o segundo.
    private static int DigitoVerificador(string digitos)
    {
        var soma = 0;
        for (var i = 0; i < digitos.Length; i++)
        {
            soma += (digitos[i] - '0') * (digitos.Length + 1 - i);
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
