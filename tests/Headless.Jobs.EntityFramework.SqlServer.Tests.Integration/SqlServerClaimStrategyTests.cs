// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Hosting.Initialization;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Sql;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimStrategyTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task locked_candidate_is_skipped_while_an_unlocked_root_is_claimed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("readpast-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var cronId = Guid.NewGuid();
            var lockedId = Guid.NewGuid();
            var availableId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "readpast", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronOccurrenceAsync(
                lockedId,
                cronId,
                (int)JobStatus.Idle,
                null,
                NodeDeathPolicy.Retry,
                null,
                DateTime.UtcNow.AddMinutes(-2),
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                availableId,
                cronId,
                (int)JobStatus.Idle,
                null,
                NodeDeathPolicy.Retry,
                null,
                DateTime.UtcNow.AddMinutes(-1),
                ct
            );

            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT [Id] FROM {fixture.QualifiedCronJobOccurrencesTable} WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = lockedId;
                command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync(ct);
            }

            var claimed = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToListAsync(ct);
            claimed.Select(x => x.Id).Should().Contain(availableId).And.NotContain(lockedId);
            await transaction.RollbackAsync(ct);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task claims_execute_when_read_committed_snapshot_is_enabled()
    {
        var ct = AbortToken;
        var databaseName = $"jobs_rcsi_{Guid.NewGuid():N}";
        var masterConnectionString = fixture.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

        var databaseCreated = false;
        IHost? host = null;
        try
        {
            await using (var connection = new SqlConnection(masterConnectionString))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{databaseName}];";
                await command.ExecuteNonQueryAsync(ct);
                databaseCreated = true;
                command.CommandText = $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON;";
                await command.ExecuteNonQueryAsync(ct);
            }

            var rcsiFixture = new SqlServerNativeClaimsFixture(databaseConnectionString);
            host = rcsiFixture.BuildHost("rcsi-a");
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var cronId = Guid.NewGuid();
            var lockedId = Guid.NewGuid();
            var availableId = Guid.NewGuid();
            await rcsiFixture.SeedCronJobAsync(cronId, "rcsi", "* * * * *", NodeDeathPolicy.Retry, ct);
            await rcsiFixture.SeedCronOccurrenceAsync(
                lockedId,
                cronId,
                (int)JobStatus.Idle,
                null,
                NodeDeathPolicy.Retry,
                null,
                DateTime.UtcNow.AddMinutes(-2),
                ct
            );
            await rcsiFixture.SeedCronOccurrenceAsync(
                availableId,
                cronId,
                (int)JobStatus.Idle,
                null,
                NodeDeathPolicy.Retry,
                null,
                DateTime.UtcNow.AddMinutes(-1),
                ct
            );

            await using var lockConnection = new SqlConnection(databaseConnectionString);
            await lockConnection.OpenAsync(ct);
            await using var lockTransaction = await lockConnection.BeginTransactionAsync(ct);
            await using (var lockCommand = lockConnection.CreateCommand())
            {
                lockCommand.Transaction = (SqlTransaction)lockTransaction;
                lockCommand.CommandText =
                    $"SELECT [Id] FROM {rcsiFixture.QualifiedCronJobOccurrencesTable} WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
                lockCommand.Parameters.Add(new SqlParameter("id", lockedId));
                await lockCommand.ExecuteScalarAsync(ct);
            }

            var claimed = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);

            claimed.Select(x => x.Id).Should().Contain(availableId).And.NotContain(lockedId);
            await lockTransaction.RollbackAsync(ct);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (host is not null)
            {
                await host.StopAsync(cleanup.Token);
                host.Dispose();
            }

            if (databaseCreated)
            {
                await using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync(cleanup.Token);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
                await command.ExecuteNonQueryAsync(cleanup.Token);
            }
        }
    }

    [Fact]
    public async Task custom_schema_table_and_column_mappings_are_used_by_native_claims()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildMappedHost<SqlServerMappedJobsDbContext>("mapped-sql-a", "mapped_jobs");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<SqlServerMappedJobsDbContext>(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "mapped",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            claimed.Should().ContainSingle().Which.Id.Should().Be(job.Id);
            claimed[0].OwnerId.Should().NotBeNullOrWhiteSpace();

            var cronId = Guid.NewGuid();
            var fallbackOccurrenceId = Guid.NewGuid();
            var factory = host.Services.GetRequiredService<IDbContextFactory<SqlServerMappedJobsDbContext>>();
            await using (var db = await factory.CreateDbContextAsync(ct))
            {
                db.Set<CronJobEntity>()
                    .Add(
                        new CronJobEntity
                        {
                            Id = cronId,
                            Function = "mapped-cron",
                            ContractVersion = "mapped-v2",
                            Request = [1, 2],
                            Expression = "* * * * *",
                        }
                    );
                db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Add(
                        new CronJobOccurrenceEntity<CronJobEntity>
                        {
                            Id = fallbackOccurrenceId,
                            CronJobId = cronId,
                            Function = "previous-mapped-cron",
                            ContractVersion = "mapped-v1",
                            Request = [7, 8],
                            ExecutionTime = DateTime.UtcNow.AddMinutes(-2),
                        }
                    );
                await db.SaveChangesAsync(ct);
            }

            var directContext = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "mapped-cron",
                Expression = "* * * * *",
            };
            var direct = await persistence
                .QueueCronJobOccurrencesAsync((DateTime.UtcNow.AddMinutes(1), [directContext]), ct)
                .ToArrayAsync(ct);
            direct.Should().ContainSingle();
            direct[0].Function.Should().Be("mapped-cron");
            direct[0].ContractVersion.Should().Be("mapped-v2");
            direct[0].Request.Should().Equal(1, 2);

            var fallback = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            fallback.Select(x => x.Id).Should().Contain(fallbackOccurrenceId);
            var stored = fallback.Single(x => x.Id == fallbackOccurrenceId);
            stored.Function.Should().Be("previous-mapped-cron");
            stored.ContractVersion.Should().Be("mapped-v1");
            stored.Request.Should().Equal(7, 8);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await host.StopAsync(cleanup.Token);
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(cleanup.Token);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "DROP TABLE IF EXISTS [mapped_jobs].[native_cron_occurrences];"
                + "DROP TABLE IF EXISTS [mapped_jobs].[native_time_jobs];"
                + "DROP TABLE IF EXISTS [mapped_jobs].[CronJobs];"
                // The reservation table follows the configured schema like every other Jobs table, so it lands here
                // too and SQL Server refuses to drop a schema that still owns it.
                + "DROP TABLE IF EXISTS [mapped_jobs].[TimeJobIdempotencyReservations];"
                + "IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'mapped_jobs') DROP SCHEMA [mapped_jobs];";
            await command.ExecuteNonQueryAsync(cleanup.Token);
        }
    }

    [Fact]
    public async Task native_claim_preserves_sub_second_lease_precision()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var leaseDuration = TimeSpan.FromMilliseconds(500);
        using var host = fixture.BuildHost("precision-sql-a", leaseDuration: leaseDuration);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "sub-second-lease",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            claimed.Should().ContainSingle().Which.Id.Should().Be(job.Id);
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT [UpdatedAt], [LockedUntil] FROM {fixture.QualifiedTimeJobsTable} WHERE [Id] = @id;";
            command.Parameters.Add(new SqlParameter("id", job.Id));
            await using var reader = await command.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).Should().BeTrue();
            var updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(0, ct);
            var persistedLeaseDuration = reader.GetDateTime(1) - updatedAt.UtcDateTime;

            persistedLeaseDuration.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(499));
            persistedLeaseDuration.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(501));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task descendant_stamp_failure_rolls_back_the_root_claim()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("rollback-sql-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TRIGGER [headless].[fail_descendant_claim] ON {fixture.QualifiedTimeJobsTable} AFTER UPDATE AS "
                + "IF EXISTS (SELECT 1 FROM inserted WHERE [Function] = 'fail-child' AND [OwnerId] IS NOT NULL) "
                + "THROW 51000, 'forced descendant failure', 1;";
            await command.ExecuteNonQueryAsync(ct);
        }

        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var child = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "fail-child",
                RunCondition = RunCondition.OnSuccess,
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "rollback-root",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
                Children = [child],
            };
            await persistence.AddTimeJobsAsync([root], ct);
            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            await claim.Should().ThrowAsync<SqlException>();

            foreach (var job in new[] { root, child })
            {
                var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
                status.Should().Be((int)JobStatus.Idle);
                ownerId.Should().BeNull();
                lockedUntil.Should().BeNull();
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task cancellation_before_commit_rolls_back_mutations()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("cancel-sql-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity { Id = Guid.NewGuid(), Function = "cancel" };
            await persistence.AddTimeJobsAsync([job], ct);
            var factory = host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
            using var cancellation = new CancellationTokenSource();

            await using (
                var claimTransaction = await JobsClaimTransaction<JobsDbContext>.CreateAsync(
                    factory,
                    new SqlAutonomousAttempt(),
                    ct
                )
            )
            {
                await using var command = claimTransaction.DbContext.Database.GetDbConnection().CreateCommand();
                command.Transaction = claimTransaction.Transaction.GetDbTransaction();
                command.CommandText =
                    $"UPDATE {fixture.QualifiedTimeJobsTable} SET [OwnerId] = 'partial' WHERE [Id] = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = job.Id;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync(ct);
                await cancellation.CancelAsync();

                var commit = async () => await claimTransaction.CommitAsync(cancellation.Token);
                await commit.Should().ThrowAsync<OperationCanceledException>();
            }

            (await fixture.ReadTimeJobDetailAsync(job.Id, ct)).OwnerId.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }
}

/// <summary>Runs the claim retry conformance suite on SQL Server, with a genuine deadlock-victim exception.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimRetryConformanceTests(SqlServerJobsCoordinationFixture fixture)
    : JobsClaimRetryConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    private const int _DeadlockVictimErrorNumber = 1205;

    [Fact]
    public override Task transient_fault_before_commit_is_retried_and_commits_correct_durable_state() =>
        base.transient_fault_before_commit_is_retried_and_commits_correct_durable_state();

    [Fact]
    public override Task transient_retries_are_bounded_and_the_driver_exception_propagates() =>
        base.transient_retries_are_bounded_and_the_driver_exception_propagates();

    [Fact]
    public override Task transient_fault_from_the_commit_is_not_retried_and_the_driver_exception_propagates() =>
        base.transient_fault_from_the_commit_is_not_retried_and_the_driver_exception_propagates();

    protected override Exception CreateTransientClaimFailure() => SqlDeadlockVictim.CreateException();

    protected override bool IsInjectedFailure(Exception exception) =>
        exception is SqlException { Number: _DeadlockVictimErrorNumber };
}

/// <summary>
/// Builds a genuine <see cref="SqlException" /> with <c>Number == 1205</c>. Microsoft.Data.SqlClient exposes no
/// public constructor, so the error, its collection, and the exception are assembled through the same internal
/// members the driver itself uses.
/// </summary>
internal static class SqlDeadlockVictim
{
    private const BindingFlags _NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static SqlException CreateException()
    {
        var errorConstructor =
            typeof(SqlError)
                .GetConstructors(_NonPublicInstance)
                .FirstOrDefault(candidate =>
                {
                    var parameters = candidate.GetParameters();

                    return parameters.Length == 9
                        && parameters[7].ParameterType == typeof(int)
                        && parameters[8].ParameterType == typeof(Exception);
                })
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes the SqlError shape.");

        var error = errorConstructor.Invoke([
            1205,
            (byte)51,
            (byte)13,
            "headless-tests",
            "Transaction (Process ID 51) was deadlocked on lock resources with another process and has been "
                + "chosen as the deadlock victim. Rerun the transaction.",
            string.Empty,
            1,
            0,
            null,
        ]);

        var errors =
            (SqlErrorCollection?)
                Activator.CreateInstance(
                    typeof(SqlErrorCollection),
                    _NonPublicInstance,
                    binder: null,
                    args: null,
                    culture: null
                )
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes SqlErrorCollection.");
        var add =
            typeof(SqlErrorCollection).GetMethod("Add", _NonPublicInstance)
            ?? throw new InvalidOperationException(
                "Microsoft.Data.SqlClient no longer exposes SqlErrorCollection.Add."
            );
        add.Invoke(errors, [error]);

        var create =
            typeof(SqlException).GetMethod(
                "CreateException",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                [typeof(SqlErrorCollection), typeof(string)],
                modifiers: null
            )
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes SqlException factory.");

        return (SqlException)create.Invoke(null, [errors, "17.00.0000"])!;
    }
}

internal sealed class SqlServerNativeClaimsFixture(string connectionString) : IJobsCoordinationFixture
{
    public string ConnectionString { get; } = connectionString;

    public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

    public string QualifiedTimeJobsTable => "[headless].[TimeJobs]";

    public string QualifiedCronJobsTable => "[headless].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[headless].[CronJobOccurrences]";

    public string QualifyTable(string schema, string table) => $"[{schema}].[{table}]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, {seconds}, SYSUTCDATETIME())");

    public string EfTranslatedDatabaseClockSql => "GETUTCDATE()";

    public string ResetSql => string.Empty;

    public string CreateProbeTableSql => string.Empty;

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlServer(ConnectionString);
    }

    public void ConfigureStore(DbContextOptionsBuilder db)
    {
        db.UseSqlServer(ConnectionString);
    }

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        builder.UseSqlServerClaims();
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(ConnectionString);
    }

    public void ConfigureUnitOfWork(IServiceCollection services)
    {
        services.AddSqlServerUnitOfWork();
    }

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer(ConnectionString);
    }

    public Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<IServiceProvider, IUnitOfWork, DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException();
    }
}

internal sealed class SqlServerMappedJobsDbContext(DbContextOptions<SqlServerMappedJobsDbContext> options)
    : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TimeJobEntity>(entity =>
        {
            entity.ToTable("native_time_jobs", "mapped_jobs");
            entity.Property(x => x.Id).HasColumnName("job_id");
            entity.Property(x => x.Status).HasColumnName("job_status");
            entity.Property(x => x.OwnerId).HasColumnName("owner_key");
            entity.Property(x => x.LockedUntil).HasColumnName("lease_until");
            entity.Property(x => x.OnNodeDeath).HasColumnName("death_policy");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_on");
            entity.Property(x => x.ExecutionTime).HasColumnName("run_on");
            entity.Property(x => x.ParentId).HasColumnName("parent_key");
        });
        modelBuilder.Entity<CronJobOccurrenceEntity<CronJobEntity>>(entity =>
        {
            entity.ToTable("native_cron_occurrences", "mapped_jobs");
            entity.Property(x => x.Id).HasColumnName("occurrence_id");
            entity.Property(x => x.Function).HasColumnName("contract_name");
            entity.Property(x => x.ContractVersion).HasColumnName("contract_version");
            entity.Property(x => x.Request).HasColumnName("contract_payload");
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_key");
            entity.Property(x => x.CausationId).HasColumnName("causation_key");
            entity.Property(x => x.Status).HasColumnName("occurrence_status");
            entity.Property(x => x.OwnerId).HasColumnName("occurrence_owner");
            entity.Property(x => x.ExecutionTime).HasColumnName("occurrence_time");
            entity.Property(x => x.CronJobId).HasColumnName("cron_key");
            entity.Property(x => x.LockedUntil).HasColumnName("occurrence_lease");
            entity.Property(x => x.OnNodeDeath).HasColumnName("occurrence_policy");
            entity.Property(x => x.ElapsedTime).HasColumnName("elapsed_ms");
            entity.Property(x => x.RetryCount).HasColumnName("retry_count");
            entity.Property(x => x.CreatedAt).HasColumnName("created_on");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_on");
            entity
                .HasIndex(x => new { x.CronJobId, x.ExecutionTime })
                .HasFilter("[occurrence_status] IN (N'Idle', N'Queued', N'InProgress')");
        });
    }
}
