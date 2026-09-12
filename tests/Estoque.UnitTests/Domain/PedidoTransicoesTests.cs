using Estoque.Api.Domain;

namespace Estoque.UnitTests.Domain;

public class PedidoTransicoesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 12, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pedido_criado_comeca_como_novo()
    {
        Assert.Equal(StatusPedido.Novo, CriarPedido(StatusPedido.Novo).Status);
    }

    [Fact]
    public void Pagamento_de_pedido_novo_muda_para_pago_e_registra_a_data()
    {
        var pedido = CriarPedido(StatusPedido.Novo);

        Assert.True(pedido.RegistrarPagamento(Agora));
        Assert.Equal(StatusPedido.Pago, pedido.Status);
        Assert.Equal(Agora, pedido.PagoEm);
    }

    [Fact]
    public void Envio_de_pedido_pago_muda_para_enviado_e_registra_a_data()
    {
        var pedido = CriarPedido(StatusPedido.Pago);

        Assert.True(pedido.RegistrarEnvio(Agora));
        Assert.Equal(StatusPedido.Enviado, pedido.Status);
        Assert.Equal(Agora, pedido.EnviadoEm);
    }

    [Theory]
    [InlineData(StatusPedido.Novo)]
    [InlineData(StatusPedido.Pago)]
    public void Cancelamento_de_pedido_novo_ou_pago_e_permitido(StatusPedido statusInicial)
    {
        var pedido = CriarPedido(statusInicial);

        Assert.True(pedido.Cancelar(Agora));
        Assert.Equal(StatusPedido.Cancelado, pedido.Status);
        Assert.Equal(Agora, pedido.CanceladoEm);
    }

    [Theory]
    [InlineData(StatusPedido.Enviado)]
    [InlineData(StatusPedido.Cancelado)]
    public void Cancelamento_de_pedido_enviado_ou_ja_cancelado_e_recusado(StatusPedido statusInicial)
    {
        var pedido = CriarPedido(statusInicial);

        Assert.False(pedido.Cancelar(Agora));
        Assert.Equal(statusInicial, pedido.Status);
    }

    [Theory]
    [InlineData(StatusPedido.Pago)]
    [InlineData(StatusPedido.Enviado)]
    [InlineData(StatusPedido.Cancelado)]
    public void Pagamento_so_e_aceito_para_pedido_novo(StatusPedido statusInicial)
    {
        var pedido = CriarPedido(statusInicial);

        Assert.False(pedido.RegistrarPagamento(Agora));
        Assert.Equal(statusInicial, pedido.Status);
    }

    [Theory]
    [InlineData(StatusPedido.Novo)]
    [InlineData(StatusPedido.Enviado)]
    [InlineData(StatusPedido.Cancelado)]
    public void Envio_so_e_aceito_para_pedido_pago(StatusPedido statusInicial)
    {
        var pedido = CriarPedido(statusInicial);

        Assert.False(pedido.RegistrarEnvio(Agora));
        Assert.Equal(statusInicial, pedido.Status);
    }

    // Leva o pedido até o status desejado pelas transições válidas, sem atalhos.
    private static Pedido CriarPedido(StatusPedido status)
    {
        var pedido = new Pedido(
            clienteNome: "Maria Souza",
            clienteCpf: "52998224725",
            clienteEmail: "maria@example.com",
            cep: "01001000",
            itens: [new ItemPedido(produtoId: 1, quantidade: 2, precoUnitario: 10m)],
            valores: new ValoresPedido(Subtotal: 20m, Desconto: 0m, Frete: 5m, Total: 25m),
            criadoEm: Agora);

        if (status is StatusPedido.Pago or StatusPedido.Enviado)
        {
            pedido.RegistrarPagamento(Agora);
        }

        if (status is StatusPedido.Enviado)
        {
            pedido.RegistrarEnvio(Agora);
        }

        if (status is StatusPedido.Cancelado)
        {
            pedido.Cancelar(Agora);
        }

        return pedido;
    }
}
