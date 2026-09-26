// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // EF migrations are generated with block-scoped namespaces.

namespace Headless.Jobs.Api.Demo.Migrations
{
    /// <summary>Adds the cron schedule watermark, dispatch projection, and misfire recovery columns.</summary>
    public partial class AddCronScheduleWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvaluationFingerprint",
                table: "CronJobs",
                type: "character varying(128)",
                maxLength: 128,
                schema: "jobs",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "FingerprintFailureCount",
                table: "CronJobs",
                type: "integer",
                schema: "jobs",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "FingerprintRetryAfterUtc",
                table: "CronJobs",
                type: "timestamp with time zone",
                schema: "jobs",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MissedRunGraceSeconds",
                table: "CronJobs",
                type: "integer",
                schema: "jobs",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "NextDueUtc",
                table: "CronJobs",
                type: "timestamp with time zone",
                schema: "jobs",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<string>(
                name: "OnMissedRun",
                table: "CronJobs",
                type: "character varying(32)",
                maxLength: 32,
                schema: "jobs",
                nullable: false,
                defaultValue: "Coalesce"
            );

            migrationBuilder.AddColumn<string>(
                name: "OnOverlap",
                table: "CronJobs",
                type: "character varying(32)",
                maxLength: 32,
                schema: "jobs",
                nullable: false,
                defaultValue: "Allow"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledThroughUtc",
                table: "CronJobs",
                type: "timestamp with time zone",
                schema: "jobs",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveredFromUtc",
                table: "CronJobOccurrences",
                type: "timestamp with time zone",
                schema: "jobs",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CronJobs_EvaluationFingerprint",
                table: "CronJobs",
                column: "EvaluationFingerprint",
                schema: "jobs"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CronJobs_FingerprintRetryAfterUtc_Id",
                table: "CronJobs",
                columns: ["FingerprintRetryAfterUtc", "Id"],
                schema: "jobs"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CronJobs_IsPaused_NextDueUtc",
                table: "CronJobs",
                columns: ["IsPaused", "NextDueUtc"],
                schema: "jobs"
            );
        }

        /// <inheritdoc />
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
                           OR "OnOverlap" <> 'Allow'
                           OR "ReconciledThroughUtc" <> '-infinity'::timestamp with time zone
                    ) OR EXISTS (
                        SELECT 1 FROM jobs."CronJobOccurrences" WHERE "RecoveredFromUtc" IS NOT NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade cron schedule watermark migration while durable schedule or recovery state exists.';
                    END IF;
                END $migration$;
                """
            );

            migrationBuilder.DropIndex(name: "IX_CronJobs_EvaluationFingerprint", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_CronJobs_FingerprintRetryAfterUtc_Id",
                table: "CronJobs",
                schema: "jobs"
            );

            migrationBuilder.DropIndex(name: "IX_CronJobs_IsPaused_NextDueUtc", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "EvaluationFingerprint", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "FingerprintFailureCount", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "FingerprintRetryAfterUtc", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "MissedRunGraceSeconds", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "NextDueUtc", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "OnMissedRun", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "OnOverlap", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "ReconciledThroughUtc", table: "CronJobs", schema: "jobs");

            migrationBuilder.DropColumn(name: "RecoveredFromUtc", table: "CronJobOccurrences", schema: "jobs");
        }
    }
}
