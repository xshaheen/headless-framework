// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.Migrations;

[DbContext(typeof(SqlServerCancellationMigrationDbContext))]
[Migration(Id)]
internal sealed class AddCronScheduleWatermark : Migration
{
    public const string Id = "20260804000100_AddCronScheduleWatermark";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "EvaluationFingerprint",
            schema: "jobs",
            table: "CronJobs",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "FingerprintFailureCount",
            schema: "jobs",
            table: "CronJobs",
            type: "int",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "FingerprintRetryAfterUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "datetime2",
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "MissedRunGraceSeconds",
            schema: "jobs",
            table: "CronJobs",
            type: "int",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "NextDueUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "datetime2",
            nullable: false,
            defaultValue: default(DateTime)
        );
        migrationBuilder.AddColumn<string>(
            name: "OnMissedRun",
            schema: "jobs",
            table: "CronJobs",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Coalesce"
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "ReconciledThroughUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "datetime2",
            nullable: false,
            defaultValue: default(DateTime)
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "RecoveredFromUtc",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "datetime2",
            nullable: true
        );
        migrationBuilder.CreateIndex(
            name: "IX_CronJobs_EvaluationFingerprint",
            schema: "jobs",
            table: "CronJobs",
            column: "EvaluationFingerprint"
        );
        migrationBuilder.CreateIndex(
            name: "IX_CronJobs_FingerprintRetryAfterUtc_Id",
            schema: "jobs",
            table: "CronJobs",
            columns: ["FingerprintRetryAfterUtc", "Id"]
        );
        migrationBuilder.CreateIndex(
            name: "IX_CronJobs_IsPaused_NextDueUtc",
            schema: "jobs",
            table: "CronJobs",
            columns: ["IsPaused", "NextDueUtc"]
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM [jobs].[CronJobs]
                WHERE [EvaluationFingerprint] IS NOT NULL
                   OR [FingerprintFailureCount] <> 0
                   OR [FingerprintRetryAfterUtc] IS NOT NULL
                   OR [MissedRunGraceSeconds] <> 0
                   OR [NextDueUtc] <> CONVERT(datetime2, '0001-01-01T00:00:00.0000000')
                   OR [OnMissedRun] <> N'Coalesce'
                   OR [ReconciledThroughUtc] <> CONVERT(datetime2, '0001-01-01T00:00:00.0000000')
            ) OR EXISTS (
                SELECT 1 FROM [jobs].[CronJobOccurrences] WHERE [RecoveredFromUtc] IS NOT NULL
            )
                THROW 51000, 'Cannot downgrade cron schedule watermark migration while durable schedule or recovery state exists.', 1;
            """
        );
        migrationBuilder.DropIndex(name: "IX_CronJobs_EvaluationFingerprint", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropIndex(name: "IX_CronJobs_FingerprintRetryAfterUtc_Id", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropIndex(name: "IX_CronJobs_IsPaused_NextDueUtc", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "EvaluationFingerprint", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "FingerprintFailureCount", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "FingerprintRetryAfterUtc", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "MissedRunGraceSeconds", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "NextDueUtc", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "OnMissedRun", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "ReconciledThroughUtc", schema: "jobs", table: "CronJobs");
        migrationBuilder.DropColumn(name: "RecoveredFromUtc", schema: "jobs", table: "CronJobOccurrences");
    }
}
