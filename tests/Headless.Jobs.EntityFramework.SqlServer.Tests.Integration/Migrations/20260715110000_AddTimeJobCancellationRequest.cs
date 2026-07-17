// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.Migrations;

[DbContext(typeof(SqlServerCancellationMigrationDbContext))]
[Migration(Id)]
internal sealed class AddTimeJobCancellationRequest : Migration
{
    public const string Id = "20260715110000_AddTimeJobCancellationRequest";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CancelRequested",
            schema: "jobs",
            table: "TimeJobs",
            type: "bit",
            nullable: false,
            defaultValue: false
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancelRequested", schema: "jobs", table: "TimeJobs");
    }
}
