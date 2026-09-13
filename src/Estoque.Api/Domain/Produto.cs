namespace Estoque.Api.Domain;

public sealed class Produto
{
    // O EF Core usa este construtor ao ler do banco: os nomes dos parâmetros batem com as propriedades.
    public Produto(string nome, string sku, decimal preco, decimal custoUnitario, int quantidade)
    {
        Nome = nome;
        Sku = sku;
        Preco = preco;
        CustoUnitario = custoUnitario;
        Quantidade = quantidade;
        Ativo = true;
    }

    public int Id { get; private set; }

    public string Nome { get; private set; }

    public string Sku { get; private set; }

    public decimal Preco { get; private set; }

    // Informação comercial interna: nunca sai nas respostas nem nos logs (RN01).
    public decimal CustoUnitario { get; private set; }

    public int Quantidade { get; private set; }

    public bool Ativo { get; private set; }

    public void Atualizar(string nome, string sku, decimal preco, decimal custoUnitario, int quantidade)
    {
        Nome = nome;
        Sku = sku;
        Preco = preco;
        CustoUnitario = custoUnitario;
        Quantidade = quantidade;
    }
}
