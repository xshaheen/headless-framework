// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Api.Demo.Migrations;

public partial class JobsCronOnNodeDeath : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OnNodeDeath",
            table: "CronJobs",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: false,
            defaultValue: "Retry"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnNodeDeath", table: "CronJobs", schema: "jobs");
    }
}
