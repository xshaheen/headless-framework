// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Cross-provider conformance for atomic job enqueue via commit coordination (separate from the distributed-lock
/// coordination suite in <c>JobsCoordinationConformanceTests</c>). Proves the EF coordinated-write seam (U2) and the
/// manager routing (U3/U4) hold identically on every relational backend:
/// <list type="bullet">
/// <item>AE1 — a domain write + <c>AddAsync</c> in one enlisted transaction commit together.</item>
/// <item>AE1 (rollback) — they discard together; the AsyncLocal-capture regression net (a stranded capture would
/// take the direct path and auto-commit the row, leaving it after the coordinated rollback).</item>
/// <item>AE2 — two enqueues in one scope both commit.</item>
/// <item>AE4 — no coordinator → <c>AddAsync</c> inserts directly.</item>
/// <item>R6 — cron <c>AddAsync</c> commits / rolls back atomically.</item>
/// </list>
/// Each leaf derives a sealed class with <c>[Collection&lt;TFixture&gt;]</c> and re-declares the methods with
/// <c>[Fact]</c> so the runner discovers them per provider.
/// </summary>
public abstract class JobsEnqueueAtomicityConformanceTests<TFixture>(TFixture fixture)
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task domain_write_and_enqueue_commit_atomically()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(
                        connection,
                        transaction,
                        innerCt
                    );
                    var result = await manager.AddAsync(_TimeJob(), innerCt);
                    result.IsSucceeded.Should().BeTrue();
                },
                ct
            );

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task rollback_discards_enqueue_and_domain_write()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(
                            connection,
                            transaction,
                            innerCt
                        );
                        await manager.AddAsync(_TimeJob(), innerCt);

                        // Abandon the scope after the writes are buffered: the transaction never commits.
                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Should()
                .BeSameAs(sentinel);

            // The regression net: a stranded AsyncLocal capture would have direct-inserted (own connection, committed)
            // and left the row behind. Both writes must be gone.
            (await fixture.CountTimeJobsAsync(ct))
                .Should()
                .Be(0);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task two_enqueues_in_one_scope_both_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) =>
                {
                    (await manager.AddAsync(_TimeJob(), innerCt)).IsSucceeded.Should().BeTrue();
                    (await manager.AddAsync(_TimeJob(), innerCt)).IsSucceeded.Should().BeTrue();
                },
                ct
            );

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task enqueue_without_coordinator_inserts_directly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            // No ambient coordinated scope: AddAsync takes the direct path and the row is immediately visible.
            var result = await manager.AddAsync(_TimeJob(), ct);

            result.IsSucceeded.Should().BeTrue();
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_enqueue_commits_atomically()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var before = await fixture.CountCronJobsAsync(ct);

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) =>
                {
                    (await manager.AddAsync(_CronJob(), innerCt)).IsSucceeded.Should().BeTrue();
                },
                ct
            );

            (await fixture.CountCronJobsAsync(ct)).Should().Be(before + 1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_enqueue_rolls_back()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var before = await fixture.CountCronJobsAsync(ct);
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, innerCt) =>
                    {
                        await manager.AddAsync(_CronJob(), innerCt);

                        throw sentinel;
                    },
                    ct
                );

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await fixture.CountCronJobsAsync(ct)).Should().Be(before);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task<IHost> _StartHostAsync(CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);
        var host = fixture.BuildCoordinatedEnqueueHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, cancellationToken);
        await fixture.CreateProbeTableAsync(cancellationToken);
        await host.StartAsync(cancellationToken);

        return host;
    }

    private static TimeJobEntity _TimeJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Request = [],
            // Far-future so the deferred immediate-dispatch branch never runs — keeps the assertion on row presence.
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };

    private static CronJobEntity _CronJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            // CronScheduleCache parses with IncludingSeconds = true, so the expression has six fields.
            Expression = "0 0 0 * * *",
            Request = [],
        };
}
