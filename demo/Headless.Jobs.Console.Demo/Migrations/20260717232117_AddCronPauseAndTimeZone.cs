using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Console.Demo.Migrations;

public partial class AddCronPauseAndTimeZone : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UQ_CronJobId_ExecutionTime", schema: "jobs", table: "CronJobOccurrences");

        migrationBuilder.AddColumn<bool>(
            name: "IsPaused",
            schema: "jobs",
            table: "CronJobs",
            type: "boolean",
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
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );

        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            columns: new[] { "CronJobId", "ExecutionTime" },
            unique: true,
            filter: "\"Status\" IN ('Idle', 'Queued', 'InProgress')"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM jobs."CronJobOccurrences"
                    GROUP BY "CronJobId", "ExecutionTime"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot downgrade cron pause/timezone migration while terminal and live occurrences share a schedule instant.';
                END IF;
            END $$;
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
            columns: new[] { "CronJobId", "ExecutionTime" },
            unique: true
        );
    }
}
