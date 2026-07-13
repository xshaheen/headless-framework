using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HeadlessShop.Catalog.Infrastructure.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("202607130001_InitialCatalog")]
public sealed class InitialCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Sku = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Products", product => product.Id)
        );

        migrationBuilder.CreateIndex(
            name: "IX_Products_TenantId_Sku",
            table: "Products",
            columns: ["TenantId", "Sku"],
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Products");
    }
}
