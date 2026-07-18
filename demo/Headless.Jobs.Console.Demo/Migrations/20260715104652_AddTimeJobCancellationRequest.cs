using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Console.Demo.Migrations;

public partial class AddTimeJobCancellationRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CancelRequested",
            table: "TimeJobs",
            type: "boolean",
            schema: "jobs",
            nullable: false,
            defaultValue: false
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancelRequested", table: "TimeJobs", schema: "jobs");
    }
}
