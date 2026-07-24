using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeadlessShop.Catalog.Infrastructure.Migrations;

public partial class InitialCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "catalog");

        migrationBuilder.CreateTable(
            name: "Products",
            schema: "catalog",
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
                table.PrimaryKey("PK_Products", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_Products_TenantId_Sku",
            schema: "catalog",
            table: "Products",
            columns: new[] { "TenantId", "Sku" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Products", schema: "catalog");
    }
}
