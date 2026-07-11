// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimStrategyTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public void rcsi_hint_includes_readcommittedlock()
    {
        SqlServerJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>
            .GetReadPastHints(readCommittedSnapshotEnabled: true)
            .Should()
            .Contain("READCOMMITTEDLOCK");
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
    public async Task descendant_stamp_failure_rolls_back_the_root_claim()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("rollback-sql-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
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

            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            await claim.Should().ThrowAsync<SqlException>();

            foreach (var job in new[] { root, child })
            {
                var detail = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
                detail.Status.Should().Be((int)JobStatus.Idle);
                detail.OwnerId.Should().BeNull();
                detail.LockedUntil.Should().BeNull();
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
}

internal sealed class SqlServerNativeClaimsFixture(string connectionString) : IJobsCoordinationFixture
{
    public string ConnectionString { get; } = connectionString;

    public string QualifiedTimeJobsTable => "[jobs].[TimeJobs]";

    public string QualifiedCronJobsTable => "[jobs].[CronJobs]";

    public string QualifiedCronJobOccurrencesTable => "[jobs].[CronJobOccurrences]";

    public string UtcNowSqlExpression => "SYSUTCDATETIME()";

    public string ResetSql => string.Empty;

    public string CreateProbeTableSql => string.Empty;

    public void ConfigureCoordination(HeadlessCoordinationSetupBuilder setup) => setup.UseSqlServer(ConnectionString);

    public void ConfigureStore(DbContextOptionsBuilder db) => db.UseSqlServer(ConnectionString);

    public void ConfigureClaims(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder) =>
        builder.UseSqlServerClaims();

    public DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    public void ConfigureCommitCoordination(IServiceCollection services) => services.AddSqlServerCommitCoordination();

    public Task RunCoordinatedTransactionAsync(
        IServiceProvider services,
        Func<DbConnection, DbTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException();
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
        });
    }
}
