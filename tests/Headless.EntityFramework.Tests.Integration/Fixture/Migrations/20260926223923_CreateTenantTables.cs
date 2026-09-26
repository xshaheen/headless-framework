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
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Text = table.Column<string>(type: "text", nullable: false),
            },
            schema: "app",
            constraints: table =>
            {
                table.PrimaryKey("PK_Notes", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "Rows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            },
            schema: "app",
            constraints: table =>
            {
                table.PrimaryKey("PK_Rows", x => x.Id);
            }
        );

        migrationBuilder.InsertData(
            table: "Rows",
            columns: ["Id", "Name", "TenantId"],
            values: [new Guid("0198f6a4-0000-7000-8000-000000000001"), "seeded", "seed"],
            schema: "app"
        );

        migrationBuilder.CreateIndex(name: "IX_Rows_Name", table: "Rows", column: "Name", schema: "app");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Notes", schema: "app");

        migrationBuilder.DropTable(name: "Rows", schema: "app");
    }
}
