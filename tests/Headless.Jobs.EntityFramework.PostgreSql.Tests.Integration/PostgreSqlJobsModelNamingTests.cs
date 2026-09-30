// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

/// <summary>
/// PostgreSQL folds unquoted identifiers to lower case, so the Jobs model must name every table, column, key, index,
/// and constraint in snake_case there, and write its raw filter and check SQL against the snake_case columns.
/// </summary>
public sealed class PostgreSqlJobsModelNamingTests : TestBase
{
    // Building the model never opens a connection, so no container is needed.
    private static readonly IModel _Model = JobsModelNaming.BuildDesignTimeModel(db =>
        db.UseNpgsql("Host=localhost;Database=model_only")
    );

    [Fact]
    public void time_jobs_table_uses_snake_case_names()
    {
        var entity = _Model.FindEntityType(typeof(TimeJobEntity))!;

        entity.GetTableName().Should().Be("time_jobs");
        entity.ColumnName(nameof(TimeJobEntity.ExecutionTime)).Should().Be("execution_time");
        entity.ColumnName(nameof(TimeJobEntity.LockedUntil)).Should().Be("locked_until");
        entity.ColumnName(nameof(TimeJobEntity.IsCurrentGeneration)).Should().Be("is_current_generation");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_time_jobs");
        entity.GetForeignKeys().Single().GetConstraintName().Should().Be("fk_time_jobs_time_jobs_parent_id");
        entity
            .IndexNames()
            .Should()
            .Contain([
                "ix_time_jobs_parent_id",
                "ix_time_jobs_status_execution_time",
                "ix_time_jobs_tenant_id_status_execution_time",
                "ux_time_jobs_key_generation_tenant",
                "ux_time_jobs_current_key_system",
            ]);
    }

    [Fact]
    public void keyed_filters_and_check_constraint_reference_snake_case_columns()
    {
        var entity = _Model.FindEntityType(typeof(TimeJobEntity))!;

        var check = entity.GetCheckConstraints().Single();
        check.Name.Should().Be("ck_time_jobs_keyed_metadata");
        check.Sql.Should().Contain("business_key IS NULL").And.Contain("is_current_generation IS NOT NULL");
        check.Sql.Should().NotContain("BusinessKey");

        entity
            .GetIndexes()
            .Single(index =>
                string.Equals(index.GetDatabaseName(), "ux_time_jobs_current_key_tenant", StringComparison.Ordinal)
            )
            .GetFilter()
            .Should()
            .Contain("tenant_id IS NOT NULL");
    }

    [Fact]
    public void cron_tables_use_snake_case_names()
    {
        var cron = _Model.FindEntityType(typeof(CronJobEntity))!;
        cron.GetTableName().Should().Be("cron_jobs");
        cron.ColumnName(nameof(CronJobEntity.NextDueUtc)).Should().Be("next_due_utc");
        cron.FindPrimaryKey()!.GetName().Should().Be("pk_cron_jobs");
        cron.IndexNames().Should().Contain("ix_cron_jobs_function_expression");

        var occurrence = _Model.FindEntityType(typeof(CronJobOccurrenceEntity<CronJobEntity>))!;
        occurrence.GetTableName().Should().Be("cron_job_occurrences");
        occurrence.ColumnName(nameof(CronJobOccurrenceEntity<>.CronJobId)).Should().Be("cron_job_id");
        occurrence.FindPrimaryKey()!.GetName().Should().Be("pk_cron_job_occurrences");
        occurrence
            .GetForeignKeys()
            .Single()
            .GetConstraintName()
            .Should()
            .Be("fk_cron_job_occurrences_cron_jobs_cron_job_id");

        var unique = occurrence
            .GetIndexes()
            .Single(index =>
                string.Equals(
                    index.GetDatabaseName(),
                    "uq_cron_job_occurrences_cron_job_id_execution_time",
                    StringComparison.Ordinal
                )
            );

        unique.GetFilter().Should().Be("\"status\" IN ('Idle', 'Queued', 'InProgress')");
    }

    [Fact]
    public void reservation_table_uses_snake_case_names()
    {
        var entity = _Model.FindEntityType(typeof(JobIdempotencyReservationEntity))!;

        entity.GetTableName().Should().Be("time_job_idempotency_reservations");
        entity.ColumnName(nameof(JobIdempotencyReservationEntity.IdempotencyKey)).Should().Be("idempotency_key");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_time_job_idempotency_reservations");
        entity.IndexNames().Should().Equal("ix_time_job_idempotency_reservations_expires_at");
    }
}
