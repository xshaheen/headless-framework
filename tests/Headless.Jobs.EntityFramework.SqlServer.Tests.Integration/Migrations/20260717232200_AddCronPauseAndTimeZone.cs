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
        migrationBuilder.DropIndex(name: "UQ_CronJobId_ExecutionTime", table: "CronJobOccurrences", schema: "jobs");
        migrationBuilder.AddColumn<bool>(
            name: "IsPaused",
            table: "CronJobs",
            type: "bit",
            schema: "jobs",
            nullable: false,
            defaultValue: false
        );
        migrationBuilder.AddColumn<long>(
            name: "ScheduleRevision",
            table: "CronJobs",
            type: "bigint",
            schema: "jobs",
            nullable: false,
            defaultValue: 0L
        );
        migrationBuilder.AddColumn<string>(
            name: "TimeZoneId",
            table: "CronJobs",
            type: "nvarchar(128)",
            maxLength: 128,
            schema: "jobs",
            nullable: true
        );
        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            table: "CronJobOccurrences",
            columns: ["CronJobId", "ExecutionTime"],
            schema: "jobs",
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
        migrationBuilder.DropIndex(name: "UQ_CronJobId_ExecutionTime", table: "CronJobOccurrences", schema: "jobs");
        migrationBuilder.DropColumn(name: "IsPaused", table: "CronJobs", schema: "jobs");
        migrationBuilder.DropColumn(name: "ScheduleRevision", table: "CronJobs", schema: "jobs");
        migrationBuilder.DropColumn(name: "TimeZoneId", table: "CronJobs", schema: "jobs");
        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            table: "CronJobOccurrences",
            columns: ["CronJobId", "ExecutionTime"],
            schema: "jobs",
            unique: true
        );
    }
}
