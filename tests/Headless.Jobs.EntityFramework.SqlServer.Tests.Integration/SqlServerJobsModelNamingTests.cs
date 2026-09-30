// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

/// <summary>
/// SQL Server keeps the Jobs model's PascalCase names: tables, columns, keys, indexes, and constraints all follow the
/// property and entity names, and the raw filter and check SQL reference the PascalCase columns.
/// </summary>
public sealed class SqlServerJobsModelNamingTests : TestBase
{
    // Building the model never opens a connection, so no container is needed.
    private static readonly IModel _Model = JobsModelNaming.BuildDesignTimeModel(db =>
        db.UseSqlServer("Server=localhost;Database=model_only")
    );

    [Fact]
    public void time_jobs_table_uses_pascal_case_names()
    {
        var entity = _Model.FindEntityType(typeof(TimeJobEntity))!;

        entity.GetTableName().Should().Be("TimeJobs");
        entity.ColumnName(nameof(TimeJobEntity.ExecutionTime)).Should().Be("ExecutionTime");
        entity.ColumnName(nameof(TimeJobEntity.LockedUntil)).Should().Be("LockedUntil");
        entity.FindPrimaryKey()!.GetName().Should().Be("PK_TimeJobs");
        entity.GetForeignKeys().Single().GetConstraintName().Should().Be("FK_TimeJobs_TimeJobs_ParentId");
        entity
            .IndexNames()
            .Should()
            .Contain([
                "IX_TimeJobs_ParentId",
                "IX_TimeJobs_Status_ExecutionTime",
                "IX_TimeJobs_TenantId_Status_ExecutionTime",
                "UX_TimeJobs_KeyGeneration_Tenant",
                "UX_TimeJobs_CurrentKey_System",
            ]);
    }

    [Fact]
    public void keyed_check_constraint_references_pascal_case_columns()
    {
        var entity = _Model.FindEntityType(typeof(TimeJobEntity))!;

        var check = entity.GetCheckConstraints().Single();
        check.Name.Should().Be("CK_TimeJobs_KeyedMetadata");
        check.Sql.Should().Contain("[BusinessKey]").And.Contain("[IsCurrentGeneration]");
    }

    [Fact]
    public void cron_tables_use_pascal_case_names()
    {
        var cron = _Model.FindEntityType(typeof(CronJobEntity))!;
        cron.GetTableName().Should().Be("CronJobs");
        cron.FindPrimaryKey()!.GetName().Should().Be("PK_CronJobs");
        cron.IndexNames().Should().Contain("IX_CronJobs_Function_Expression");

        var occurrence = _Model.FindEntityType(typeof(CronJobOccurrenceEntity<CronJobEntity>))!;
        occurrence.GetTableName().Should().Be("CronJobOccurrences");
        occurrence.ColumnName(nameof(CronJobOccurrenceEntity<>.CronJobId)).Should().Be("CronJobId");
        occurrence
            .GetForeignKeys()
            .Single()
            .GetConstraintName()
            .Should()
            .Be("FK_CronJobOccurrences_CronJobs_CronJobId");
        occurrence
            .GetIndexes()
            .Single(index =>
                string.Equals(
                    index.GetDatabaseName(),
                    "UQ_CronJobOccurrences_CronJobId_ExecutionTime",
                    StringComparison.Ordinal
                )
            )
            .GetFilter()
            .Should()
            .Be("\"Status\" IN ('Idle', 'Queued', 'InProgress')");
    }

    [Fact]
    public void reservation_table_uses_pascal_case_names()
    {
        var entity = _Model.FindEntityType(typeof(JobIdempotencyReservationEntity))!;

        entity.GetTableName().Should().Be("TimeJobIdempotencyReservations");
        entity.FindPrimaryKey()!.GetName().Should().Be("PK_TimeJobIdempotencyReservations");
        entity.IndexNames().Should().Equal("IX_TimeJobIdempotencyReservations_ExpiresAt");
    }
}
