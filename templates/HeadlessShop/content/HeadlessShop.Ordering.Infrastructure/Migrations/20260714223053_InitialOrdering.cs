using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeadlessShop.Ordering.Infrastructure.Migrations;

public partial class InitialOrdering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "ordering");

        migrationBuilder.CreateTable(
            name: "Orders",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                DateCreated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "ProductSnapshots",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Price = table.Column<decimal>(type: "numeric(18,2)", precision: 32, scale: 10, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductSnapshots", x => x.Id);
            }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Orders", schema: "ordering");

        migrationBuilder.DropTable(name: "ProductSnapshots", schema: "ordering");
    }
}
