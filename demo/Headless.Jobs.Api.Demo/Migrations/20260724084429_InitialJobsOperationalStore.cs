using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Api.Demo.Migrations;

/// <summary>Defines the initial Jobs operational store migration.</summary>
public partial class InitialJobsOperationalStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "jobs");

        migrationBuilder.CreateTable(
            name: "CronJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Expression = table.Column<string>(type: "text", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                IsPaused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                ScheduleRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                Request = table.Column<byte[]>(type: "bytea", nullable: true),
                Retries = table.Column<int>(type: "integer", nullable: false),
                RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                OnNodeDeath = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Function = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                InitIdentifier = table.Column<string>(type: "text", nullable: true),
                TenantId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            schema: "jobs",
            constraints: table =>
            {
                table.PrimaryKey("PK_CronJobs", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "TimeJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Function = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                InitIdentifier = table.Column<string>(type: "text", nullable: true),
                TenantId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerId = table.Column<string>(type: "text", nullable: true),
                Request = table.Column<byte[]>(type: "bytea", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancelRequested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                OnNodeDeath = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExceptionMessage = table.Column<string>(type: "text", nullable: true),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                Retries = table.Column<int>(type: "integer", nullable: false),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                RunCondition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
            },
            schema: "jobs",
            constraints: table =>
            {
                table.PrimaryKey("PK_TimeJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_TimeJobs_TimeJobs_ParentId",
                    column: x => x.ParentId,
                    principalTable: "TimeJobs",
                    principalColumn: "Id",
                    principalSchema: "jobs"
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "CronJobOccurrences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerId = table.Column<string>(type: "text", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CronJobId = table.Column<Guid>(type: "uuid", nullable: false),
                LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                OnNodeDeath = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExceptionMessage = table.Column<string>(type: "text", nullable: true),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            schema: "jobs",
            constraints: table =>
            {
                table.PrimaryKey("PK_CronJobOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_CronJobOccurrences_CronJobs_CronJobId",
                    column: x => x.CronJobId,
                    principalTable: "CronJobs",
                    principalColumn: "Id",
                    principalSchema: "jobs",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_CronJobId",
            table: "CronJobOccurrences",
            column: "CronJobId",
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_ExecutionTime",
            table: "CronJobOccurrences",
            column: "ExecutionTime",
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_OwnerId_Status",
            table: "CronJobOccurrences",
            columns: new[] { "OwnerId", "Status" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_Status_ExecutionTime",
            table: "CronJobOccurrences",
            columns: new[] { "Status", "ExecutionTime" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobOccurrence_Status_LockedUntil",
            table: "CronJobOccurrences",
            columns: new[] { "Status", "LockedUntil" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            table: "CronJobOccurrences",
            columns: new[] { "CronJobId", "ExecutionTime" },
            schema: "jobs",
            unique: true,
            filter: "\"Status\" IN ('Idle', 'Queued', 'InProgress')"
        );

        migrationBuilder.CreateIndex(
            name: "IX_CronJobs_Expression",
            table: "CronJobs",
            column: "Expression",
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_Function_Expression",
            table: "CronJobs",
            columns: new[] { "Function", "Expression" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_ExecutionTime",
            table: "TimeJobs",
            column: "ExecutionTime",
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_OwnerId_Status",
            table: "TimeJobs",
            columns: new[] { "OwnerId", "Status" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_Status_ExecutionTime",
            table: "TimeJobs",
            columns: new[] { "Status", "ExecutionTime" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_Status_LockedUntil",
            table: "TimeJobs",
            columns: new[] { "Status", "LockedUntil" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_TenantId_Status_ExecutionTime",
            table: "TimeJobs",
            columns: new[] { "TenantId", "Status", "ExecutionTime" },
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJobs_ParentId",
            table: "TimeJobs",
            column: "ParentId",
            schema: "jobs"
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
