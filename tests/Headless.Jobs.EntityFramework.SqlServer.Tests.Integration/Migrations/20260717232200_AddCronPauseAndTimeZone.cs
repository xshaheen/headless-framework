// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.Migrations;

[DbContext(typeof(SqlServerCancellationMigrationDbContext))]
[Migration(Id)]
internal sealed class AddCronPauseAndTimeZone : Migration
{
    public const string Id = "20260717232200_AddCronPauseAndTimeZone";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UQ_CronJobId_ExecutionTime", schema: "jobs", table: "CronJobOccurrences");
        migrationBuilder.AddColumn<bool>(
            name: "IsPaused",
            schema: "jobs",
            table: "CronJobs",
            type: "bit",
            nullable: false,
            defaultValue: false
        );
        migrationBuilder.AddColumn<long>(
            name: "ScheduleRevision",
            schema: "jobs",
            table: "CronJobs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );
        migrationBuilder.AddColumn<string>(
            name: "TimeZoneId",
            schema: "jobs",
            table: "CronJobs",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true
        );
        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            columns: ["CronJobId", "ExecutionTime"],
            unique: true,
            filter: "[Status] IN (N'Idle', N'Queued', N'InProgress')"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM [jobs].[CronJobOccurrences]
                GROUP BY [CronJobId], [ExecutionTime]
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51000, 'Cannot downgrade cron pause/timezone migration while terminal and live occurrences share a schedule instant.', 1;
            """
        );
        migrationBuilder.DropIndex(name: "UQ_CronJobId_ExecutionTime", schema: "jobs", table: "CronJobOccurrences");
        migrationBuilder.DropColumn(name: "IsPaused", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "ScheduleRevision", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "TimeZoneId", schema: "jobs", table: "CronJobs");
        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            columns: ["CronJobId", "ExecutionTime"],
            unique: true
        );
    }
}
