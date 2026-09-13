using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Estoque.Api.Data.Migrations;

/// <inheritdoc />
public partial class AmpliaPrecisaoDosValoresDoPedido : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "total",
            table: "pedidos",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(12,2)",
            oldPrecision: 12,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "subtotal",
            table: "pedidos",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(12,2)",
            oldPrecision: 12,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "frete",
            table: "pedidos",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(12,2)",
            oldPrecision: 12,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "desconto",
            table: "pedidos",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(12,2)",
            oldPrecision: 12,
            oldScale: 2);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "total",
            table: "pedidos",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "subtotal",
            table: "pedidos",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "frete",
            table: "pedidos",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.AlterColumn<decimal>(
            name: "desconto",
            table: "pedidos",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);
    }
}
