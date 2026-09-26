using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tests.Fixture.Migrations;

/// <summary>Widens the indexed row name column from 100 to 200 characters.</summary>
public partial class WidenRowName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            schema: "app",
            table: "Rows",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            schema: "app",
            table: "Rows",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200
        );
    }
}
