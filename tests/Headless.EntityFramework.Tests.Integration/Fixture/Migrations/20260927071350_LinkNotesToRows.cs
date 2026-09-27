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
        migrationBuilder.AddColumn<Guid>(name: "RowId", schema: "app", table: "Notes", type: "uuid", nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Notes_RowId", schema: "app", table: "Notes", column: "RowId");

        migrationBuilder.AddForeignKey(
            name: "FK_Notes_Rows_RowId",
            schema: "app",
            table: "Notes",
            column: "RowId",
            principalSchema: "app",
            principalTable: "Rows",
            principalColumn: "Id"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Notes_Rows_RowId", schema: "app", table: "Notes");

        migrationBuilder.DropIndex(name: "IX_Notes_RowId", schema: "app", table: "Notes");

        migrationBuilder.DropColumn(name: "RowId", schema: "app", table: "Notes");
    }
}
