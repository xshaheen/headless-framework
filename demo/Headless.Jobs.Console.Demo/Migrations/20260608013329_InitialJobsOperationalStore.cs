using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Console.Demo.Migrations;

public partial class InitialJobsOperationalStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "jobs");

        migrationBuilder.CreateTable(
            name: "CronJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Expression = table.Column<string>(type: "text", nullable: false),
                Request = table.Column<byte[]>(type: "bytea", nullable: true),
                Retries = table.Column<int>(type: "integer", nullable: false),
                RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                Function = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                InitIdentifier = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                Description = table.Column<string>(type: "text", nullable: false),
                InitIdentifier = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                LockHolder = table.Column<string>(type: "text", nullable: true),
                Request = table.Column<byte[]>(type: "bytea", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExceptionMessage = table.Column<string>(type: "text", nullable: true),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                Retries = table.Column<int>(type: "integer", nullable: false),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                RetryIntervals = table.Column<int[]>(type: "integer[]", nullable: true),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                RunCondition = table.Column<int>(type: "integer", nullable: true),
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
                Status = table.Column<int>(type: "integer", nullable: false),
                LockHolder = table.Column<string>(type: "text", nullable: true),
                ExecutionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CronJobId = table.Column<Guid>(type: "uuid", nullable: false),
                LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExceptionMessage = table.Column<string>(type: "text", nullable: true),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                ElapsedTime = table.Column<long>(type: "bigint", nullable: false),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
            name: "IX_CronJobOccurrence_Status_ExecutionTime",
            table: "CronJobOccurrences",
            columns: ["Status", "ExecutionTime"],
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "UQ_CronJobId_ExecutionTime",
            table: "CronJobOccurrences",
            columns: ["CronJobId", "ExecutionTime"],
            schema: "jobs",
            unique: true
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
            columns: ["Function", "Expression"],
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_ExecutionTime",
            table: "TimeJobs",
            column: "ExecutionTime",
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJob_Status_ExecutionTime",
            table: "TimeJobs",
            columns: ["Status", "ExecutionTime"],
            schema: "jobs"
        );

        migrationBuilder.CreateIndex(
            name: "IX_TimeJobs_ParentId",
            table: "TimeJobs",
            column: "ParentId",
            schema: "jobs"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CronJobOccurrences", schema: "jobs");

        migrationBuilder.DropTable(name: "TimeJobs", schema: "jobs");

        migrationBuilder.DropTable(name: "CronJobs", schema: "jobs");
    }
}
