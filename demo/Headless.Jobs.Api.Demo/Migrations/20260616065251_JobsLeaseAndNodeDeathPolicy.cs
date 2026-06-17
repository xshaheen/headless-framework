// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Api.Demo.Migrations;

public partial class JobsLeaseAndNodeDeathPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "LockedAt", schema: "jobs", table: "TimeJobs", newName: "LockedUntil");

        migrationBuilder.RenameColumn(name: "LockHolder", schema: "jobs", table: "TimeJobs", newName: "OwnerId");

        migrationBuilder.RenameColumn(
            name: "LockedAt",
            schema: "jobs",
            table: "CronJobOccurrences",
            newName: "LockedUntil"
        );

        migrationBuilder.RenameColumn(
            name: "LockHolder",
            schema: "jobs",
            table: "CronJobOccurrences",
            newName: "OwnerId"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            schema: "jobs",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer"
        );

        migrationBuilder.AlterColumn<string>(
            name: "RunCondition",
            schema: "jobs",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "OnNodeDeath",
            schema: "jobs",
            table: "TimeJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Retry"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer"
        );

        migrationBuilder.AddColumn<string>(
            name: "OnNodeDeath",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Retry"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnNodeDeath", schema: "jobs", table: "TimeJobs");

        migrationBuilder.DropColumn(name: "OnNodeDeath", schema: "jobs", table: "CronJobOccurrences");

        migrationBuilder.RenameColumn(name: "OwnerId", schema: "jobs", table: "TimeJobs", newName: "LockHolder");

        migrationBuilder.RenameColumn(name: "LockedUntil", schema: "jobs", table: "TimeJobs", newName: "LockedAt");

        migrationBuilder.RenameColumn(
            name: "OwnerId",
            schema: "jobs",
            table: "CronJobOccurrences",
            newName: "LockHolder"
        );

        migrationBuilder.RenameColumn(
            name: "LockedUntil",
            schema: "jobs",
            table: "CronJobOccurrences",
            newName: "LockedAt"
        );

        migrationBuilder.AlterColumn<int>(
            name: "Status",
            schema: "jobs",
            table: "TimeJobs",
            type: "integer",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32
        );

        migrationBuilder.AlterColumn<int>(
            name: "RunCondition",
            schema: "jobs",
            table: "TimeJobs",
            type: "integer",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true
        );

        migrationBuilder.AlterColumn<int>(
            name: "Status",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "integer",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32
        );
    }
}
