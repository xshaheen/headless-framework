using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tests.Fixture.Migrations;

/// <summary>Links each note to an optional row, adding a cross-table foreign key inside the schema.</summary>
public partial class LinkNotesToRows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "RowId", table: "Notes", type: "uuid", schema: "app", nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Notes_RowId", table: "Notes", column: "RowId", schema: "app");

        migrationBuilder.AddForeignKey(
            name: "FK_Notes_Rows_RowId",
            table: "Notes",
            column: "RowId",
            principalTable: "Rows",
            schema: "app",
            principalSchema: "app",
            principalColumn: "Id"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Notes_Rows_RowId", table: "Notes", schema: "app");

        migrationBuilder.DropIndex(name: "IX_Notes_RowId", table: "Notes", schema: "app");

        migrationBuilder.DropColumn(name: "RowId", table: "Notes", schema: "app");
    }
}
