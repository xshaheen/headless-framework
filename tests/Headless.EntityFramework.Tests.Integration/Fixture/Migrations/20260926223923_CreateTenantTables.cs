using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tests.Fixture.Migrations;

/// <summary>Creates the tenant rows and notes tables, with one seeded row.</summary>
public partial class CreateTenantTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "app");

        migrationBuilder.CreateTable(
            name: "Notes",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Text = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Notes", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "Rows",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Rows", x => x.Id);
            }
        );

        migrationBuilder.InsertData(
            schema: "app",
            table: "Rows",
            columns: new[] { "Id", "Name", "TenantId" },
            values: new object[] { new Guid("0198f6a4-0000-7000-8000-000000000001"), "seeded", "seed" }
        );

        migrationBuilder.CreateIndex(name: "IX_Rows_Name", schema: "app", table: "Rows", column: "Name");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Notes", schema: "app");

        migrationBuilder.DropTable(name: "Rows", schema: "app");
    }
}
