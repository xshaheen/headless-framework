using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headless.Jobs.Console.Demo.Migrations;

/// <summary>Creates the Jobs operational store schema.</summary>
public partial class InitialJobsOperationalStore : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "headless");

        migrationBuilder.CreateTable(
            name: "cron_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                expression = table.Column<string>(type: "text", nullable: false),
                time_zone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                is_paused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                schedule_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                reconciled_through_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                next_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                evaluation_fingerprint = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true
                ),
                fingerprint_failure_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                fingerprint_retry_after_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                missed_run_grace_seconds = table.Column<int>(type: "integer", nullable: false),
                on_missed_run = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "Coalesce"
                ),
                on_overlap = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "Allow"
                ),
                request = table.Column<byte[]>(type: "bytea", nullable: true),
                retries = table.Column<int>(type: "integer", nullable: false),
                retry_intervals = table.Column<int[]>(type: "integer[]", nullable: true),
                on_node_death = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                function = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false,
                    collation: "C"
                ),
                contract_version = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false,
                    collation: "C"
                ),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                init_identifier = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            schema: "headless",
            constraints: table =>
            {
                table.PrimaryKey("pk_cron_jobs", x => x.id);
            }
        );

        migrationBuilder.CreateTable(
            name: "time_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                function = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false,
                    collation: "C"
                ),
                contract_version = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false,
                    collation: "C"
                ),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                init_identifier = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: true,
                    collation: "C"
                ),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                business_key = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: true,
                    collation: "C"
                ),
                intent_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                fingerprint_algorithm = table.Column<string>(
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: true
                ),
                generation = table.Column<long>(type: "bigint", nullable: true),
                is_current_generation = table.Column<bool>(type: "boolean", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                owner_id = table.Column<string>(type: "text", nullable: true),
                request = table.Column<byte[]>(type: "bytea", nullable: true),
                execution_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancel_requested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                on_node_death = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                exception_message = table.Column<string>(type: "text", nullable: true),
                skipped_reason = table.Column<string>(type: "text", nullable: true),
                elapsed_time = table.Column<long>(type: "bigint", nullable: false),
                retries = table.Column<int>(type: "integer", nullable: false),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                retry_intervals = table.Column<int[]>(type: "integer[]", nullable: true),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                run_condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
            },
            schema: "headless",
            constraints: table =>
            {
                table.PrimaryKey("pk_time_jobs", x => x.id);
                table.CheckConstraint(
                    "ck_time_jobs_keyed_metadata",
                    "(business_key IS NULL AND intent_fingerprint IS NULL AND fingerprint_algorithm IS NULL AND generation IS NULL AND is_current_generation IS NULL) OR (business_key IS NOT NULL AND business_key <> '' AND intent_fingerprint IS NOT NULL AND intent_fingerprint <> '' AND fingerprint_algorithm IS NOT NULL AND fingerprint_algorithm <> '' AND generation IS NOT NULL AND generation > 0 AND is_current_generation IS NOT NULL AND parent_id IS NULL AND run_condition IS NULL)"
                );
                table.ForeignKey(
                    name: "fk_time_jobs_time_jobs_parent_id",
                    column: x => x.parent_id,
                    principalTable: "time_jobs",
                    principalColumn: "id",
                    principalSchema: "headless"
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "cron_job_occurrences",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                function = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false,
                    collation: "C"
                ),
                contract_version = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false,
                    collation: "C"
                ),
                request = table.Column<byte[]>(type: "bytea", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                owner_id = table.Column<string>(type: "text", nullable: true),
                execution_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                cron_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                on_node_death = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                exception_message = table.Column<string>(type: "text", nullable: true),
                skipped_reason = table.Column<string>(type: "text", nullable: true),
                disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                elapsed_time = table.Column<long>(type: "bigint", nullable: false),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                recovered_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            schema: "headless",
            constraints: table =>
            {
                table.PrimaryKey("pk_cron_job_occurrences", x => x.id);
                table.ForeignKey(
                    name: "fk_cron_job_occurrences_cron_jobs_cron_job_id",
                    column: x => x.cron_job_id,
                    principalTable: "cron_jobs",
                    principalColumn: "id",
                    principalSchema: "headless",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_job_occurrences_cron_job_id",
            table: "cron_job_occurrences",
            column: "cron_job_id",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_job_occurrences_execution_time",
            table: "cron_job_occurrences",
            column: "execution_time",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_job_occurrences_owner_id_status",
            table: "cron_job_occurrences",
            columns: ["owner_id", "status"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_job_occurrences_status_execution_time",
            table: "cron_job_occurrences",
            columns: ["status", "execution_time"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_job_occurrences_status_locked_until",
            table: "cron_job_occurrences",
            columns: ["status", "locked_until"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "uq_cron_job_occurrences_cron_job_id_execution_time",
            table: "cron_job_occurrences",
            columns: ["cron_job_id", "execution_time"],
            schema: "headless",
            unique: true,
            filter: "\"status\" IN ('Idle', 'Queued', 'InProgress')"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_jobs_evaluation_fingerprint",
            table: "cron_jobs",
            column: "evaluation_fingerprint",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_jobs_expression",
            table: "cron_jobs",
            column: "expression",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_jobs_fingerprint_retry_after_utc_id",
            table: "cron_jobs",
            columns: ["fingerprint_retry_after_utc", "id"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_jobs_function_expression",
            table: "cron_jobs",
            columns: ["function", "expression"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_cron_jobs_is_paused_next_due_utc",
            table: "cron_jobs",
            columns: ["is_paused", "next_due_utc"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_execution_time",
            table: "time_jobs",
            column: "execution_time",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_owner_id_status",
            table: "time_jobs",
            columns: ["owner_id", "status"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_parent_id",
            table: "time_jobs",
            column: "parent_id",
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_status_execution_time",
            table: "time_jobs",
            columns: ["status", "execution_time"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_status_locked_until",
            table: "time_jobs",
            columns: ["status", "locked_until"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ix_time_jobs_tenant_id_status_execution_time",
            table: "time_jobs",
            columns: ["tenant_id", "status", "execution_time"],
            schema: "headless"
        );

        migrationBuilder.CreateIndex(
            name: "ux_time_jobs_current_key_system",
            table: "time_jobs",
            columns: ["function", "business_key"],
            schema: "headless",
            unique: true,
            filter: "business_key IS NOT NULL AND tenant_id IS NULL AND is_current_generation = TRUE"
        );

        migrationBuilder.CreateIndex(
            name: "ux_time_jobs_current_key_tenant",
            table: "time_jobs",
            columns: ["tenant_id", "function", "business_key"],
            schema: "headless",
            unique: true,
            filter: "business_key IS NOT NULL AND tenant_id IS NOT NULL AND is_current_generation = TRUE"
        );

        migrationBuilder.CreateIndex(
            name: "ux_time_jobs_key_generation_system",
            table: "time_jobs",
            columns: ["function", "business_key", "generation"],
            schema: "headless",
            unique: true,
            filter: "business_key IS NOT NULL AND tenant_id IS NULL"
        );

        migrationBuilder.CreateIndex(
            name: "ux_time_jobs_key_generation_tenant",
            table: "time_jobs",
            columns: ["tenant_id", "function", "business_key", "generation"],
            schema: "headless",
            unique: true,
            filter: "business_key IS NOT NULL AND tenant_id IS NOT NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "cron_job_occurrences", schema: "headless");

        migrationBuilder.DropTable(name: "time_jobs", schema: "headless");

        migrationBuilder.DropTable(name: "cron_jobs", schema: "headless");
    }
}
