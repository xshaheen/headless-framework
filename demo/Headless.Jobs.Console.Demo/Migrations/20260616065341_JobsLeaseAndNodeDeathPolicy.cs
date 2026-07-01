// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Console.Demo.Migrations;

public partial class JobsLeaseAndNodeDeathPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "LockedAt", table: "TimeJobs", newName: "LockedUntil", schema: "jobs");

        migrationBuilder.RenameColumn(name: "LockHolder", table: "TimeJobs", newName: "OwnerId", schema: "jobs");

        migrationBuilder.RenameColumn(
            name: "LockedAt",
            table: "CronJobOccurrences",
            newName: "LockedUntil",
            schema: "jobs"
        );

        migrationBuilder.RenameColumn(
            name: "LockHolder",
            table: "CronJobOccurrences",
            newName: "OwnerId",
            schema: "jobs"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer"
        );

        migrationBuilder.AlterColumn<string>(
            name: "RunCondition",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "OnNodeDeath",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: false,
            defaultValue: "Retry"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "CronJobOccurrences",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer"
        );

        migrationBuilder.AddColumn<string>(
            name: "OnNodeDeath",
            table: "CronJobOccurrences",
            type: "character varying(32)",
            maxLength: 32,
            schema: "jobs",
            nullable: false,
            defaultValue: "Retry"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnNodeDeath", table: "TimeJobs", schema: "jobs");

        migrationBuilder.DropColumn(name: "OnNodeDeath", table: "CronJobOccurrences", schema: "jobs");

        migrationBuilder.RenameColumn(name: "OwnerId", table: "TimeJobs", newName: "LockHolder", schema: "jobs");

        migrationBuilder.RenameColumn(name: "LockedUntil", table: "TimeJobs", newName: "LockedAt", schema: "jobs");

        migrationBuilder.RenameColumn(
            name: "OwnerId",
            table: "CronJobOccurrences",
            newName: "LockHolder",
            schema: "jobs"
        );

        migrationBuilder.RenameColumn(
            name: "LockedUntil",
            table: "CronJobOccurrences",
            newName: "LockedAt",
            schema: "jobs"
        );

        migrationBuilder.AlterColumn<int>(
            name: "Status",
            table: "TimeJobs",
            type: "integer",
            schema: "jobs",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32
        );

        migrationBuilder.AlterColumn<int>(
            name: "RunCondition",
            table: "TimeJobs",
            type: "integer",
            schema: "jobs",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true
        );

        migrationBuilder.AlterColumn<int>(
            name: "Status",
            table: "CronJobOccurrences",
            type: "integer",
            schema: "jobs",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32
        );
    }
}
