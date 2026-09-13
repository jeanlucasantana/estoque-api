using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estoque.Api.Data.Migrations;

/// <inheritdoc />
public partial class AdicionaChavesDeIdempotencia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "chaves_idempotencia",
            columns: table => new
            {
                chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                hash_da_requisicao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                pedido_id = table.Column<int>(type: "integer", nullable: false),
                criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chaves_idempotencia", x => x.chave);
                table.ForeignKey(
                    name: "fk_chaves_idempotencia_pedidos_pedido_id",
                    column: x => x.pedido_id,
                    principalTable: "pedidos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_chaves_idempotencia_pedido_id",
            table: "chaves_idempotencia",
            column: "pedido_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "chaves_idempotencia");
    }
}
