using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Estoque.Api.Domain;
using Estoque.Api.Features.Pedidos;
using Estoque.Api.Features.Produtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Estoque.IntegrationTests;

public sealed class LogsTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // RN08: a resposta sai antes, e a confirmação é registrada depois, pelo serviço em segundo plano.
    [Fact]
    public async Task Confirmacao_do_pedido_e_enviada_em_segundo_plano()
    {
        using var cliente = api.CriarClienteAutenticado();
        var produto = await CriarProdutoAsync(cliente, quantidade: 10);

        var pedido = await CriarPedidoAsync(cliente, produto.Id, "Cliente Confirmacao", NovoCpfValido(), $"confirmacao-{Guid.NewGuid():N}@example.com");
        var confirmacao = await EsperarConfirmacaoAsync(pedido.Id);

        Assert.Equal(LogLevel.Information, confirmacao.Level);
    }

    // RN07: nenhum log pode conter nome, CPF ou email do cliente.
    [Fact]
    public async Task Nenhum_log_contem_nome_cpf_ou_email_do_cliente()
    {
        using var cliente = api.CriarClienteAutenticado();
        var nome = $"Cliente Sigiloso {Guid.NewGuid():N}";
        var cpf = NovoCpfValido();
        var email = $"sigilo-{Guid.NewGuid():N}@example.com";
        var produto = await CriarProdutoAsync(cliente, quantidade: 2);

        // Passa por todos os caminhos que registram logs: criação, confirmação, pagamento, cancelamento,
        // recusa por estoque, falha do frete e erro de validação.
        var pedido = await CriarPedidoAsync(cliente, produto.Id, nome, cpf, email);
        (await cliente.PostAsync($"/api/pedidos/{pedido.Id}/pagamento", content: null, Ct)).EnsureSuccessStatusCode();
        (await cliente.PostAsync($"/api/pedidos/{pedido.Id}/cancelamento", content: null, Ct)).EnsureSuccessStatusCode();
        var semEstoque = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, 100, nome, cpf, email, "01001000"), Ct);
        var freteComErro = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, 1, nome, cpf, email, FreteFalso.CepComErro), Ct);
        var invalido = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produto.Id, 0, nome, cpf, email, "01001000"), Ct);
        await EsperarConfirmacaoAsync(pedido.Id);

        var registros = api.Services.GetFakeLogCollector().GetSnapshot();

        Assert.Equal(HttpStatusCode.Conflict, semEstoque.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, freteComErro.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.NotEmpty(registros);
        Assert.All(registros, registro =>
        {
            var valores = registro.StructuredState?.Select(par => par.Value) ?? Enumerable.Empty<string?>();
            var texto = $"{registro.Message} {string.Join(' ', valores)} {registro.Exception}";
            Assert.DoesNotContain(nome, texto);
            Assert.DoesNotContain(cpf, texto);
            Assert.DoesNotContain(email, texto);
        });
    }

    // Espera o log do serviço em segundo plano; o limite só evita que uma falha trave a execução dos testes.
    private async Task<FakeLogRecord> EsperarConfirmacaoAsync(int pedidoId)
    {
        var coletor = api.Services.GetFakeLogCollector();
        var id = pedidoId.ToString(CultureInfo.InvariantCulture);
        var cronometro = Stopwatch.StartNew();
        FakeLogRecord? confirmacao = null;

        while (confirmacao is null && cronometro.Elapsed < TimeSpan.FromSeconds(10))
        {
            confirmacao = coletor.GetSnapshot().FirstOrDefault(registro =>
                registro.Category == typeof(ProcessadorDeConfirmacoes).FullName
                && registro.StructuredState?.Any(par => par.Key == "PedidoId" && par.Value == id) == true);

            if (confirmacao is null)
            {
                await Task.Delay(50, Ct);
            }
        }

        Assert.NotNull(confirmacao);
        return confirmacao;
    }

    private static object Payload(int produtoId, int quantidade, string nome, string cpf, string email, string cep) => new
    {
        clienteNome = nome,
        clienteCpf = cpf,
        clienteEmail = email,
        cep,
        itens = new[] { new { produtoId, quantidade } },
    };

    private static async Task<ProdutoResponse> CriarProdutoAsync(HttpClient cliente, int quantidade)
    {
        var sku = "L" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var resposta = await cliente.PostAsJsonAsync("/api/produtos", new { nome = $"Produto de log {sku}", sku, preco = 5m, custoUnitario = 1m, quantidade }, Ct);
        resposta.EnsureSuccessStatusCode();

        var produto = await resposta.Content.ReadFromJsonAsync<ProdutoResponse>(Ct);
        Assert.NotNull(produto);
        return produto;
    }

    private static async Task<PedidoResponse> CriarPedidoAsync(HttpClient cliente, int produtoId, string nome, string cpf, string email)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", Payload(produtoId, 1, nome, cpf, email, "01001000"), Ct);
        resposta.EnsureSuccessStatusCode();

        var pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json, Ct);
        Assert.NotNull(pedido);
        return pedido;
    }

    // CPF aleatório com dígitos verificadores válidos, para que o dado procurado nos logs seja exclusivo deste teste.
    private static string NovoCpfValido()
    {
        var digitos = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        digitos[0] = digitos[0] == digitos[1] ? (digitos[0] + 1) % 10 : digitos[0];
        digitos.Add(DigitoVerificador(digitos));
        digitos.Add(DigitoVerificador(digitos));

        var cpf = string.Concat(digitos);
        Assert.True(Cpf.EhValido(cpf));
        return cpf;
    }

    private static int DigitoVerificador(List<int> digitos)
    {
        var soma = 0;
        for (var i = 0; i < digitos.Count; i++)
        {
            soma += digitos[i] * (digitos.Count + 1 - i);
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
