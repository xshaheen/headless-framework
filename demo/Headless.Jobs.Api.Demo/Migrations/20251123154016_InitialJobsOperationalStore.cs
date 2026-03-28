using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Sample.WebApi.Migrations;

/// <inheritdoc />
public partial class InitialJobsOperationalStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "jobs");

        migrationBuilder.CreateTable(
            name: "CronJobs",
            schema: "jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Expression = table.Column<string>(type: "TEXT", nullable: true),
                Request = table.Column<byte[]>(type: "BLOB", nullable: true),
                Retries = table.Column<int>(type: "INTEGER", nullable: false),
                RetryIntervals = table.Column<string>(type: "TEXT", nullable: true),
                Function = table.Column<string>(type: "TEXT", nullable: true),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                InitIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CronJobs", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "TimeJobs",
            schema: "jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Function = table.Column<string>(type: "TEXT", nullable: true),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                InitIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                LockHolder = table.Column<string>(type: "TEXT", nullable: true),
                Request = table.Column<byte[]>(type: "BLOB", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                LockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExceptionMessage = table.Column<string>(type: "TEXT", nullable: true),
                SkippedReason = table.Column<string>(type: "TEXT", nullable: true),
                ElapsedTime = table.Column<long>(type: "INTEGER", nullable: false),
                Retries = table.Column<int>(type: "INTEGER", nullable: false),
                RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                RetryIntervals = table.Column<string>(type: "TEXT", nullable: true),
                ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                RunCondition = table.Column<int>(type: "INTEGER", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TimeJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_TimeJobs_TimeJobs_ParentId",
                    column: x => x.ParentId,
                    principalSchema: "jobs",
                    principalTable: "TimeJobs",
                    principalColumn: "Id"
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "CronJobOccurrences",
            schema: "jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                LockHolder = table.Column<string>(type: "TEXT", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                CronJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                LockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExceptionMessage = table.Column<string>(type: "TEXT", nullable: true),
                SkippedReason = table.Column<string>(type: "TEXT", nullable: true),
                ElapsedTime = table.Column<long>(type: "INTEGER", nullable: false),
                RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CronJobOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_CronJobOccurrences_CronJobs_CronJobId",
                    column: x => x.CronJobId,
                    principalSchema: "jobs",
                    principalTable: "CronJobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_CronJobId",
            schema: "jobs",
            table: "CronJobOccurrences",
            column: "CronJobId"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            column: "ExecutionTime"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_Status_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            columns: new[] { "Status", "ExecutionTime" }
        );

        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            schema: "jobs",
            table: "CronJobOccurrences",
            columns: new[] { "CronJobId", "ExecutionTime" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobs_Expression",
            schema: "jobs",
            table: "CronJobs",
            column: "Expression"
        );

        migrationBuilder.CreateIndex(
            name: "IX_Function_Expression",
            schema: "jobs",
            table: "CronJobs",
            columns: new[] { "Function", "Expression" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_ExecutionTime",
            schema: "jobs",
            table: "TimeJobs",
            column: "ExecutionTime"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_Status_ExecutionTime",
            schema: "jobs",
            table: "TimeJobs",
            columns: new[] { "Status", "ExecutionTime" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJobs_ParentId",
            schema: "jobs",
            table: "TimeJobs",
            column: "ParentId"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CronJobOccurrences", schema: "jobs");

        migrationBuilder.DropTable(name: "TimeJobs", schema: "jobs");

        migrationBuilder.DropTable(name: "CronJobs", schema: "jobs");
    }
}
