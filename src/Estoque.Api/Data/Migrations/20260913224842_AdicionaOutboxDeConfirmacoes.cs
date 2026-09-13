using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Estoque.Api.Data.Migrations;

/// <inheritdoc />
public partial class AdicionaOutboxDeConfirmacoes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "confirmacoes_pendentes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                pedido_id = table.Column<int>(type: "integer", nullable: false),
                criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                tentativas = table.Column<int>(type: "integer", nullable: false),
                proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                enviada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_confirmacoes_pendentes", x => x.id);
                table.ForeignKey(
                    name: "fk_confirmacoes_pendentes_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_confirmacoes_pendentes_pedido_id",
            table: "confirmacoes_pendentes",
            column: "pedido_id");

        migrationBuilder.CreateIndex(
            name: "ix_confirmacoes_pendentes_proxima_tentativa_em",
            table: "confirmacoes_pendentes",
            column: "proxima_tentativa_em",
            filter: "enviada_em IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "confirmacoes_pendentes");
    }
}
