// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlClaimStrategyTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task locked_candidate_is_skipped_while_an_unlocked_root_is_claimed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("skip-locked-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var locked = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "locked",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-2),
            };
            var available = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "available",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([locked, available], ct);

            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT \"Id\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id FOR UPDATE;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = locked.Id;
                command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync(ct);
            }

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToListAsync(ct);
            claimed.Select(x => x.Id).Should().Contain(available.Id).And.NotContain(locked.Id);
            await transaction.RollbackAsync(ct);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task custom_schema_table_and_column_mappings_are_used_by_native_claims()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildMappedHost<PostgreSqlMappedJobsDbContext>("mapped-pg-a", "mapped_jobs");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<PostgreSqlMappedJobsDbContext>(host, ct);
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
            var factory = host.Services.GetRequiredService<IDbContextFactory<PostgreSqlMappedJobsDbContext>>();
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
            command.CommandText = "DROP SCHEMA IF EXISTS mapped_jobs CASCADE;";
            await command.ExecuteNonQueryAsync(cleanup.Token);
        }
    }

    [Fact]
    public async Task descendant_stamp_failure_rolls_back_the_root_claim()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("rollback-pg-a");
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
                    "CREATE OR REPLACE FUNCTION jobs.fail_descendant_claim() RETURNS trigger LANGUAGE plpgsql AS $$ "
                    + "BEGIN IF NEW.\"Function\" = 'fail-child' AND NEW.\"OwnerId\" IS NOT NULL THEN "
                    + "RAISE EXCEPTION 'forced descendant failure'; END IF; RETURN NEW; END $$; "
                    + $"CREATE TRIGGER fail_descendant_claim BEFORE UPDATE ON {fixture.QualifiedTimeJobsTable} "
                    + "FOR EACH ROW EXECUTE FUNCTION jobs.fail_descendant_claim();";
                await command.ExecuteNonQueryAsync(ct);
            }

            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            await claim.Should().ThrowAsync<PostgresException>();

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
        using var host = fixture.BuildHost("cancel-pg-a");
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
                    $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"OwnerId\" = 'partial' WHERE \"Id\" = @id;";
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

internal sealed class PostgreSqlMappedJobsDbContext(DbContextOptions<PostgreSqlMappedJobsDbContext> options)
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
