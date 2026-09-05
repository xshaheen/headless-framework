// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract partial class JobsTransactionalKeyedConformanceTests<TFixture>
{
    public virtual Task preflight_rejects_owned_or_different_configured_connections_without_touching_caller(
        bool onConfiguring,
        bool owned
    ) =>
        _WithHostAsync(async host =>
        {
            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, ct) =>
                {
                    await using var different = fixture.CreateConnection();
                    var parsed = new DbConnectionStringBuilder { ConnectionString = different.ConnectionString };
                    parsed[parsed.ContainsKey("Initial Catalog") ? "Initial Catalog" : "Database"] =
                        "jobs_different_database";
                    different.ConnectionString = parsed.ConnectionString;
                    var configured = owned ? connection : different;
                    OverrideJobsDbContext.ConnectionOverride = onConfiguring ? configured : null;
                    OverrideJobsDbContext.OwnsConnection = owned;
                    try
                    {
                        using (
                            var incompatible = fixture.BuildCoordinatedEnqueueHost<OverrideJobsDbContext>(
                                "preflight-reject",
                                options =>
                                {
                                    if (!onConfiguring)
                                    {
                                        var relational = RelationalOptionsExtension
                                            .Extract(options.Options)
                                            .WithConnection(configured, owned);
                                        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                                            relational
                                        );
                                    }
                                }
                            )
                        )
                        {
                            var manager = incompatible.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
                            var candidate = new TimeJobEntity
                            {
                                Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                                ExecutionTime = _Due.UtcDateTime,
                                RequireAtomicEnlistment = true,
                            };
                            var write = () => manager.AddAsync(candidate, ct);
                            await write
                                .Should()
                                .ThrowAsync<InvalidOperationException>()
                                .WithMessage(owned ? "*must not own*" : "*database differs*");
                            connection
                                .State.Should()
                                .Be(
                                    ConnectionState.Open,
                                    "preflight disposal must preserve the caller before host disposal"
                                );
                        }
                        // Assert after the rejected context AND its host are disposed: neither may close the caller's handle.
                        connection.State.Should().Be(ConnectionState.Open);
                        transaction.Connection.Should().BeSameAs(connection);
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                    }
                    finally
                    {
                        OverrideJobsDbContext.ConnectionOverride = null;
                    }
                },
                AbortToken
            );
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(0);
        });

    public virtual Task same_database_on_configuring_override_borrows_exact_caller_handles(bool keyed) =>
        _WithHostAsync(async host =>
        {
            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, ct) =>
                {
                    await using var configured = fixture.CreateConnection();
                    var observer = new BorrowedHandleObserver(connection, transaction);
                    OverrideJobsDbContext.ConnectionOverride = configured;
                    OverrideJobsDbContext.OwnsConnection = false;
                    try
                    {
                        using (
                            var compatible = fixture.BuildCoordinatedEnqueueHost<OverrideJobsDbContext>(
                                "preflight-compatible",
                                options => options.AddInterceptors(observer)
                            )
                        )
                        {
                            if (keyed)
                            {
                                (await _ScheduleAsync(compatible, new JobKey("configured-override"), "first", ct))
                                    .IsProvisional.Should()
                                    .BeTrue();
                            }
                            else
                            {
                                var manager = compatible.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
                                await manager.AddAsync(
                                    new TimeJobEntity
                                    {
                                        Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                                        ExecutionTime = _Due.UtcDateTime,
                                        RequireAtomicEnlistment = true,
                                    },
                                    ct
                                );
                            }
                            observer.Writes.Should().Be(1);
                        }
                        connection.State.Should().Be(ConnectionState.Open);
                        transaction.Connection.Should().BeSameAs(connection);
                        configured.State.Should().Be(ConnectionState.Closed);
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                    }
                    finally
                    {
                        OverrideJobsDbContext.ConnectionOverride = null;
                    }
                },
                AbortToken
            );
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(1);
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
        });

    public virtual Task post_commit_restart_failure_keeps_deadline_recoverable_by_polling()
    {
        var scheduler = new ThrowingRestartScheduler();
        return _WithHostAsync(
            async host =>
            {
                scheduler.Armed = true;
                JobScheduleResult? deadline = null;
                await fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, ct) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        deadline = await _ScheduleAsync(
                            host,
                            new JobKey("restart-failure"),
                            "first",
                            ct,
                            due: DateTimeOffset.UtcNow.AddMinutes(-2)
                        );
                        scheduler.Failures.Should().Be(0);
                    },
                    AbortToken
                );
                scheduler.Failures.Should().Be(1);
                (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(1);
                (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
                var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
                var claimed = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);
                var claimedId = claimed.Should().ContainSingle().Which.Id;
                claimedId.Should().Be(deadline!.RunId!.Value);
                (await store.GetTimeJobByIdAsync(claimedId, AbortToken))!.BusinessKey.Should().Be("restart-failure");
            },
            configureServices: services => services.AddSingleton<IJobsHostScheduler>(scheduler)
        );
    }

    public virtual Task keyed_due_eligibility_and_claim_lease_use_store_time_under_node_skew() =>
        _WithHostAsync(
            async host =>
            {
                JobScheduleResult? future = null;
                JobScheduleResult? eligible = null;
                var before = await _ReadStoreUtcNowAsync();
                ((FastNodeClock)host.Services.GetRequiredService<TimeProvider>()).UtcNow = before.AddHours(1);
                await fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, ct) =>
                    {
                        future = await _ScheduleAsync(
                            host,
                            new JobKey("store-future"),
                            "first",
                            ct,
                            due: before.AddMinutes(20)
                        );
                        eligible = await _ScheduleAsync(
                            host,
                            new JobKey("store-due"),
                            "first",
                            ct,
                            due: before.AddMinutes(-2)
                        );
                    },
                    AbortToken
                );
                var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
                var stored = await store.GetTimeJobByIdAsync(future!.RunId!.Value, AbortToken);
                var beforeClaim = await _ReadStoreUtcNowAsync();
                // SQL Server's EF-translated GETUTCDATE rounds to milliseconds; allow precision, not node/store skew.
                var precision = TimeSpan.FromMilliseconds(10);
                stored!.CreatedAt.Should().BeOnOrAfter(before - precision).And.BeOnOrBefore(beforeClaim + precision);
                var claimed = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);
                var afterClaim = await _ReadStoreUtcNowAsync();
                var due = claimed.Should().ContainSingle().Which;
                due.Id.Should().Be(eligible!.RunId!.Value);
                // Pickup projections contain execution inputs; the persisted row owns claim and key metadata.
                var leased = await store.GetTimeJobByIdAsync(due.Id, AbortToken);
                leased!.BusinessKey.Should().Be("store-due");
                leased.LockedUntil.Should().NotBeNull();
                var leaseDuration = host.Services.GetRequiredService<SchedulerOptionsBuilder>().LeaseDuration;
                leased
                    .LockedUntil!.Value.Should()
                    .BeOnOrAfter((beforeClaim + leaseDuration - precision).UtcDateTime)
                    .And.BeOnOrBefore((afterClaim + leaseDuration + precision).UtcDateTime);
            },
            clock: new FastNodeClock()
        );

    private async Task<DateTimeOffset> _ReadStoreUtcNowAsync()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression};";
        var value = await command.ExecuteScalarAsync(AbortToken);
        return value switch
        {
            DateTimeOffset instant => instant,
            DateTime instant => new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The fixture's server UTC expression did not return an instant."),
        };
    }

    private sealed class OverrideJobsDbContext(DbContextOptions<OverrideJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
    {
        public static DbConnection? ConnectionOverride { get; set; }
        public static bool OwnsConnection { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (ConnectionOverride is not null)
            {
                var relational = RelationalOptionsExtension
                    .Extract(optionsBuilder.Options)
                    .WithConnection(ConnectionOverride, OwnsConnection);
                ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(relational);
            }
        }
    }

    private sealed class BorrowedHandleObserver(DbConnection connection, DbTransaction transaction)
        : SaveChangesInterceptor
    {
        public int Writes { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            eventData.Context!.Database.GetDbConnection().Should().BeSameAs(connection);
            eventData.Context.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(transaction);
            Writes++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FastNodeClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow.AddHours(1);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ThrowingRestartScheduler : IJobsHostScheduler
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public bool IsRunning => false;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RestartIfNeeded(DateTime? dueAtStoreUtc) { }

        public void Restart()
        {
            if (Armed)
            {
                Failures++;
                throw new InjectedFailureException();
            }
        }
    }
}
