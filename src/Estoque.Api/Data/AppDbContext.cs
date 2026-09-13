using Estoque.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Estoque.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Propriedade de sombra mapeada pelo Npgsql para a coluna de sistema xmin, que muda a cada UPDATE da linha.
    // Serve de token de concorrência otimista sem poluir as entidades de domínio.
    private const string Versao = "Versao";

    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigurarProduto(modelBuilder.Entity<Produto>());
        ConfigurarPedido(modelBuilder.Entity<Pedido>());
        ConfigurarItemPedido(modelBuilder.Entity<ItemPedido>());
    }

    private static void ConfigurarProduto(EntityTypeBuilder<Produto> produto)
    {
        produto.ToTable(tabela =>
        {
            // Última linha de defesa da RN03: mesmo com um bug na aplicação, o banco recusa estoque negativo.
            tabela.HasCheckConstraint("ck_produtos_quantidade_nao_negativa", "quantidade >= 0");
            tabela.HasCheckConstraint("ck_produtos_preco_positivo", "preco > 0");
            tabela.HasCheckConstraint("ck_produtos_custo_nao_negativo", "custo_unitario >= 0");
        });

        produto.Property(p => p.Nome).HasMaxLength(120);
        produto.Property(p => p.Sku).HasMaxLength(30);
        produto.HasIndex(p => p.Sku).IsUnique();
        produto.Property(p => p.Preco).HasPrecision(12, 2);
        produto.Property(p => p.CustoUnitario).HasPrecision(12, 2);

        // Um PUT baseado em uma leitura antiga não sobrescreve uma reserva de estoque feita no meio do caminho.
        produto.Property<uint>(Versao).IsRowVersion();
    }

    private static void ConfigurarPedido(EntityTypeBuilder<Pedido> pedido)
    {
        pedido.Property(p => p.ClienteNome).HasMaxLength(200);
        pedido.Property(p => p.ClienteCpf).HasMaxLength(11);
        pedido.Property(p => p.ClienteEmail).HasMaxLength(254);
        pedido.Property(p => p.Cep).HasMaxLength(8);

        // Texto em vez de número: legível em consultas e imune a uma reordenação do enum.
        pedido.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        pedido.HasIndex(p => p.Status);

        // Totais do pedido com mais dígitos que o preço unitário: somam até 100 itens de até 1.000.000 unidades.
        pedido.Property(p => p.Subtotal).HasPrecision(18, 2);
        pedido.Property(p => p.Desconto).HasPrecision(18, 2);
        pedido.Property(p => p.Frete).HasPrecision(18, 2);
        pedido.Property(p => p.Total).HasPrecision(18, 2);

        pedido.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);

        // Dois cancelamentos simultâneos do mesmo pedido: só o primeiro grava, então o estoque não volta duas vezes.
        pedido.Property<uint>(Versao).IsRowVersion();
    }

    private static void ConfigurarItemPedido(EntityTypeBuilder<ItemPedido> item)
    {
        item.ToTable("itens_pedido", tabela =>
            tabela.HasCheckConstraint("ck_itens_pedido_quantidade_positiva", "quantidade > 0"));

        item.Property(i => i.Sku).HasMaxLength(30);
        item.Property(i => i.PrecoUnitario).HasPrecision(12, 2);

        // Produto nunca é apagado fisicamente (RN01); o Restrict protege o histórico dos pedidos.
        item.HasOne<Produto>().WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}
