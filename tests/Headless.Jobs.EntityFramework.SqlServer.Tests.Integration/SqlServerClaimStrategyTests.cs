// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimStrategyTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    private const int _DeadlockVictimErrorNumber = 1205;
    private const string _DeadlockRetryEventName = "JobsClaimDeadlockRetry";

    [Fact]
    public void rcsi_hint_includes_readcommittedlock()
    {
        SqlServerJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>
            .GetReadPastHints(readCommittedSnapshotEnabled: true)
            .Should()
            .Contain("READCOMMITTEDLOCK");
    }

    [Fact]
    public async Task concurrent_and_repeated_claims_probe_rcsi_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("rcsi-probe-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var strategy = host.Services.GetRequiredService<
                SqlServerJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>
            >();

            await Task.WhenAll(
                Enumerable
                    .Range(0, 8)
                    .Select(async _ => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
            );
            await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);

            strategy.ReadPastHintsProbeCount.Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

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
                            Expression = "* * * * *",
                        }
                    );
                db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Add(
                        new CronJobOccurrenceEntity<CronJobEntity>
                        {
                            Id = fallbackOccurrenceId,
                            CronJobId = cronId,
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

            var fallback = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            fallback.Select(x => x.Id).Should().Contain(fallbackOccurrenceId);
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
                $"CREATE TRIGGER [jobs].[fail_descendant_claim] ON {fixture.QualifiedTimeJobsTable} AFTER UPDATE AS "
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

            await using (var claimTransaction = await JobsClaimTransaction<JobsDbContext>.CreateAsync(factory, ct))
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

    [Fact]
    public async Task deadlocked_claim_scope_is_retried_and_commits_correct_durable_state()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new DeadlockVictimInterceptor(failuresToInject: 1);
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildNativeClaimHost("deadlock-retry-a", fault, logs);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "deadlock-retry",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            fault.Arm();

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            // The retry path really ran: the first scope was victimized, the second committed.
            fault.InjectedFailureCount.Should().Be(1);
            fault.CommitAttemptCount.Should().Be(2);
            logs.CountOf(_DeadlockRetryEventName).Should().Be(1);
            claimed.Should().ContainSingle().Which.Id.Should().Be(job.Id);
            claimed[0].OwnerId.Should().NotBeNullOrWhiteSpace();
            var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Queued);
            ownerId.Should().Be(claimed[0].OwnerId);
            lockedUntil.Should().NotBeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task deadlock_retries_are_bounded_and_the_sql_exception_propagates()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new DeadlockVictimInterceptor(failuresToInject: int.MaxValue);
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildNativeClaimHost("deadlock-retry-b", fault, logs);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "deadlock-exhausted",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            fault.Arm();

            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            (await claim.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(_DeadlockVictimErrorNumber);
            // One initial attempt plus the strategy's two retries — the budget is bounded, not infinite.
            fault.InjectedFailureCount.Should().Be(3);
            logs.CountOf(_DeadlockRetryEventName).Should().Be(2);
            var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Idle);
            ownerId.Should().BeNull();
            lockedUntil.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// Mirrors the harness host wiring but keeps native SQL Server claiming ON while attaching an interceptor and a
    /// log sink. <see cref="JobsCoordinationFixtureExtensions.BuildInterceptedHost" /> cannot be reused here: it
    /// deliberately turns native claiming off, and the native claim scope is exactly what is under test.
    /// </summary>
    private IHost _BuildNativeClaimHost(string nodeId, IInterceptor interceptor, ILoggerProvider logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(logs);

        builder.Services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureCoordination(setup);
            setup.Configure(options =>
            {
                options.ClusterName = JobsCoordinationFixtureExtensions.ClusterName;
                options.ConfiguredNodeId = nodeId;
                options.HeartbeatInterval = JobsCoordinationFixtureExtensions.HeartbeatInterval;
                options.SuspicionThreshold = JobsCoordinationFixtureExtensions.SuspicionThreshold;
                options.DeadThreshold = JobsCoordinationFixtureExtensions.DeadThreshold;
                options.DeadRetentionWindow = JobsCoordinationFixtureExtensions.DeadRetentionWindow;
                options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<JobsDbContext>(
                    db =>
                    {
                        fixture.ConfigureStore(db);
                        db.AddInterceptors(interceptor);
                    },
                    "jobs"
                );
                fixture.ConfigureClaims(ef);
            });
        });

        return builder.Build();
    }
}

/// <summary>
/// Fails the claim scope the way SQL Server fails a deadlock victim: a <see cref="SqlException" /> carrying error
/// 1205. The injection point is the EF transaction commit rather than a <c>DbCommandInterceptor</c> because the
/// native claim statements are raw ADO commands built off the underlying connection and never reach EF's command
/// interception pipeline; committing is the last EF-observable step inside the retried scope, so a failure there
/// discards the whole attempt exactly as a real victimization does.
/// </summary>
internal sealed class DeadlockVictimInterceptor(int failuresToInject) : DbCommandInterceptor, IDbTransactionInterceptor
{
    // Transactions that carried an EF-issued command. The native claim scope issues none — it builds raw ADO
    // commands off the underlying connection — so this is what separates a claim commit from an unrelated EF
    // write (the dead-owner reclaimer's ExecuteUpdate, seeding, coordination bookkeeping) sharing the host.
    private readonly ConcurrentDictionary<DbTransaction, byte> _efTouchedTransactions = new();
    private int _armed;
    private int _commitAttempts;
    private int _injectedFailures;

    /// <summary>Claim-scope commits observed after <see cref="Arm" />, including the ones that were failed.</summary>
    public int CommitAttemptCount => Volatile.Read(ref _commitAttempts);

    /// <summary>Deadlock victim errors actually thrown — proves the retry path was exercised, not skipped.</summary>
    public int InjectedFailureCount => Volatile.Read(ref _injectedFailures);

    /// <summary>Starts faulting; called after seeding so setup writes commit normally.</summary>
    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (
            Volatile.Read(ref _armed) == 1
            && !_efTouchedTransactions.ContainsKey(transaction)
            && Interlocked.Increment(ref _commitAttempts) <= failuresToInject
        )
        {
            Interlocked.Increment(ref _injectedFailures);

            throw SqlDeadlockVictim.CreateException();
        }

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _TrackTransaction(command);

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        _TrackTransaction(command);

        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        _TrackTransaction(command);

        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void _TrackTransaction(DbCommand command)
    {
        if (command.Transaction is { } transaction)
        {
            _efTouchedTransactions.TryAdd(transaction, 0);
        }
    }
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

/// <summary>Captures the event names of emitted log entries so a test can assert on retry observability.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _eventNames = new();

    public int CountOf(string eventName) =>
        _eventNames.Count(name => string.Equals(name, eventName, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_eventNames);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> eventNames) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!string.IsNullOrEmpty(eventId.Name))
            {
                eventNames.Enqueue(eventId.Name);
            }
        }
    }
}

internal sealed class SqlServerNativeClaimsFixture(string connectionString) : IJobsCoordinationFixture
{
    public string ConnectionString { get; } = connectionString;

    public string QualifiedTimeJobsTable => "[jobs].[TimeJobs]";

    public string QualifiedCronJobsTable => "[jobs].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[jobs].[CronJobOccurrences]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    public string UtcNowOffsetSqlExpression(int seconds) =>
        FormattableString.Invariant($"DATEADD(second, {seconds}, SYSUTCDATETIME())");

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

    public void ConfigureCommitCoordination(IServiceCollection services)
    {
        services.AddSqlServerCommitCoordination();
    }

    public void ConfigureMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer(ConnectionString);
    }

    public Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
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
