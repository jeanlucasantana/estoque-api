using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Estoque.Api.Data.Migrations;

/// <inheritdoc />
public partial class Inicial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pedidos",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                cliente_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                cliente_cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                cliente_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                desconto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                frete = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                pago_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancelado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pedidos", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "produtos",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                sku = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                preco = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                custo_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                quantidade = table.Column<int>(type: "integer", nullable: false),
                ativo = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_produtos", x => x.id);
                table.CheckConstraint("ck_produtos_custo_nao_negativo", "custo_unitario >= 0");
                table.CheckConstraint("ck_produtos_preco_positivo", "preco > 0");
                table.CheckConstraint("ck_produtos_quantidade_nao_negativa", "quantidade >= 0");
            });

        migrationBuilder.CreateTable(
            name: "itens_pedido",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                pedido_id = table.Column<int>(type: "integer", nullable: false),
                produto_id = table.Column<int>(type: "integer", nullable: false),
                quantidade = table.Column<int>(type: "integer", nullable: false),
                preco_unitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_itens_pedido", x => x.id);
                table.CheckConstraint("ck_itens_pedido_quantidade_positiva", "quantidade > 0");
                table.ForeignKey(
                    name: "fk_itens_pedido_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_itens_pedido_produtos_produto_id",
                    column: x => x.produto_id,
                    principalTable: "produtos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_itens_pedido_pedido_id",
            table: "itens_pedido",
            column: "pedido_id");

        migrationBuilder.CreateIndex(
            name: "ix_itens_pedido_produto_id",
            table: "itens_pedido",
            column: "produto_id");

        migrationBuilder.CreateIndex(
            name: "ix_pedidos_status",
            table: "pedidos",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "ix_produtos_sku",
            table: "produtos",
            column: "sku",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "itens_pedido");

        migrationBuilder.DropTable(
            name: "pedidos");

        migrationBuilder.DropTable(
            name: "produtos");
    }
}
