// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.Migrations.PostgreSql;

[DbContext(typeof(PostgreSqlWatermarkMigrationDbContext))]
[Migration(Id)]
internal sealed class PostgreSqlAddCronScheduleWatermark : Migration
{
    public const string Id = "20260804000100_AddCronScheduleWatermark";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "EvaluationFingerprint",
            schema: "jobs",
            table: "CronJobs",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "FingerprintFailureCount",
            schema: "jobs",
            table: "CronJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "FingerprintRetryAfterUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "MissedRunGraceSeconds",
            schema: "jobs",
            table: "CronJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "NextDueUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
        );
        migrationBuilder.AddColumn<string>(
            name: "OnMissedRun",
            schema: "jobs",
            table: "CronJobs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Coalesce"
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "ReconciledThroughUtc",
            schema: "jobs",
            table: "CronJobs",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "RecoveredFromUtc",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "timestamp with time zone",
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
            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM jobs."CronJobs"
                    WHERE "EvaluationFingerprint" IS NOT NULL
                       OR "FingerprintFailureCount" <> 0
                       OR "FingerprintRetryAfterUtc" IS NOT NULL
                       OR "MissedRunGraceSeconds" <> 0
                       OR "NextDueUtc" <> '-infinity'::timestamp with time zone
                       OR "OnMissedRun" <> 'Coalesce'
                       OR "ReconciledThroughUtc" <> '-infinity'::timestamp with time zone
                ) OR EXISTS (
                    SELECT 1 FROM jobs."CronJobOccurrences" WHERE "RecoveredFromUtc" IS NOT NULL
                ) THEN
                    RAISE EXCEPTION 'Cannot downgrade cron schedule watermark migration while durable schedule or recovery state exists.';
                END IF;
            END $migration$;
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
