// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // EF migrations are generated with block-scoped namespaces.

namespace Headless.Jobs.Console.Demo.Migrations
{
    /// <summary>Adds the cron schedule watermark, dispatch projection, and misfire recovery columns.</summary>
    public partial class AddCronScheduleWatermark : Migration
    {
        /// <inheritdoc />
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
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
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
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
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
                name: "IX_CronJobs_IsPaused_NextDueUtc",
                schema: "jobs",
                table: "CronJobs",
                columns: new[] { "IsPaused", "NextDueUtc" }
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

            migrationBuilder.DropIndex(name: "IX_CronJobs_IsPaused_NextDueUtc", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "EvaluationFingerprint", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "MissedRunGraceSeconds", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "NextDueUtc", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "OnMissedRun", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "ReconciledThroughUtc", schema: "jobs", table: "CronJobs");

            migrationBuilder.DropColumn(name: "RecoveredFromUtc", schema: "jobs", table: "CronJobOccurrences");
        }
    }
}
