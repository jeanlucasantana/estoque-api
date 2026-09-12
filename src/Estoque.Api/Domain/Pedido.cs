namespace Estoque.Api.Domain;

public enum StatusPedido
{
    Novo,
    Pago,
    Enviado,
    Cancelado,
}

public sealed class Pedido
{
    private readonly List<ItemPedido> _itens = [];

    // Usado pelo EF Core ao ler do banco.
    private Pedido()
    {
    }

    public Pedido(
        string clienteNome,
        string clienteCpf,
        string clienteEmail,
        string cep,
        IEnumerable<ItemPedido> itens,
        ValoresPedido valores,
        DateTimeOffset criadoEm)
    {
        ClienteNome = clienteNome;
        ClienteCpf = clienteCpf;
        ClienteEmail = clienteEmail;
        Cep = cep;
        _itens.AddRange(itens);
        Subtotal = valores.Subtotal;
        Desconto = valores.Desconto;
        Frete = valores.Frete;
        Total = valores.Total;
        Status = StatusPedido.Novo;
        CriadoEm = criadoEm;
    }

    public int Id { get; private set; }

    public string ClienteNome { get; private set; } = string.Empty;

    public string ClienteCpf { get; private set; } = string.Empty;

    public string ClienteEmail { get; private set; } = string.Empty;

    public string Cep { get; private set; } = string.Empty;

    public StatusPedido Status { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal Desconto { get; private set; }

    public decimal Frete { get; private set; }

    public decimal Total { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset? PagoEm { get; private set; }

    public DateTimeOffset? EnviadoEm { get; private set; }

    public DateTimeOffset? CanceladoEm { get; private set; }

    public IReadOnlyList<ItemPedido> Itens => _itens;

    // RN06. Transição inválida é um resultado esperado (vira 409), não uma falha: por isso bool em vez de exceção.
    public bool RegistrarPagamento(DateTimeOffset agora)
    {
        if (Status != StatusPedido.Novo)
        {
            return false;
        }

        Status = StatusPedido.Pago;
        PagoEm = agora;
        return true;
    }

    public bool RegistrarEnvio(DateTimeOffset agora)
    {
        if (Status != StatusPedido.Pago)
        {
            return false;
        }

        Status = StatusPedido.Enviado;
        EnviadoEm = agora;
        return true;
    }

    // Devolver o estoque é responsabilidade de quem chama, na mesma transação que grava o cancelamento.
    public bool Cancelar(DateTimeOffset agora)
    {
        if (Status is not (StatusPedido.Novo or StatusPedido.Pago))
        {
            return false;
        }

        Status = StatusPedido.Cancelado;
        CanceladoEm = agora;
        return true;
    }
}

public sealed class ItemPedido(int produtoId, int quantidade, decimal precoUnitario)
{
    public int Id { get; private set; }

    public int PedidoId { get; private set; }

    public int ProdutoId { get; private set; } = produtoId;

    public int Quantidade { get; private set; } = quantidade;

    // Preço vigente no momento da criação do pedido (RN04), gravado no item.
    public decimal PrecoUnitario { get; private set; } = precoUnitario;

    public Produto? Produto { get; private set; }
}
