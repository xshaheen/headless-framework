// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Provider conformance for idempotent enqueue: the reservation lifecycle (new, hit, expired race, scope and
/// descriptor isolation, terminal-state independence, coordinated rollback, process-retry shape) must hold
/// identically on PostgreSQL and SQL Server through the real scheduler surface.
/// </summary>
public abstract class JobsIdempotentEnqueueConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly TimeSpan _Ttl = TimeSpan.FromMinutes(10);

    private async Task<IHost> _StartHostAsync(string nodeName, CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);
        var host = fixture.BuildCoordinatedEnqueueHost(nodeName);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, cancellationToken);
        await fixture.CreateProbeTableAsync(cancellationToken);
        await host.StartAsync(cancellationToken);
        return host;
    }

    private static JobOptions _Options(string key, TimeSpan? ttl = null) =>
        new() { IdempotencyKey = key, IdempotencyTtl = ttl ?? _Ttl };

    public virtual async Task same_key_inside_ttl_dedups_to_first_job()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync("idem-a", ct);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var first = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "first"),
                _Options("dedup-key"),
                ct
            );
            var second = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "second-payload"),
                _Options("dedup-key"),
                ct
            );

            second.Should().Be(first, "a repeat inside the TTL observes the reservation, payload is not identity");
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task expired_reservation_is_replaced_by_exactly_one_new_job()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync("idem-b", ct);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var first = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "first"),
                _Options("expired-key"),
                ct
            );

            // Expire the reservation by directly backdating it: the TTL is hours-scale for tests, and the
            // store clock is the authority, so move ExpiresAt into the past through the reservation set.
            await _BackdateReservationAsync(host, "expired-key", ct);

            var callers = 8;
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var results = new Guid[callers];
            var tasks = Enumerable
                .Range(0, callers)
                .Select(async i =>
                {
                    await barrier.Task.WaitAsync(ct);
                    results[i] = await scheduler.EnqueueAsync(
                        new CoordinatedFacadeRequest(Guid.NewGuid(), $"race-{i}"),
                        _Options("expired-key"),
                        ct
                    );
                })
                .ToArray();
            // Release every caller at once so they contend on the advisory lock as closely as possible.
            barrier.SetResult();
            await Task.WhenAll(tasks);

            var distinct = results.Distinct().ToArray();
            distinct.Length.Should().Be(1, "post-expiry contenders must all observe the one winner");
            var winner = distinct[0];
            winner.Should().NotBe(first, "the expired reservation's job is not the new winner");
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2, "first job + exactly one replacement");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task different_descriptor_or_scope_does_not_dedup()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync("idem-c", ct);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();

            // Different descriptor: the coordinated function is a second registered function in this host.
            var facade = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "v"),
                _Options("shared-key"),
                ct
            );
            // Different descriptor: the plain (requestless) coordinated function is a second registered function
            // in this host; its descriptor must match the registered canonical shape exactly.
            var plain = await scheduler.EnqueueAsync(
                new JobFunctionDescriptor(
                    JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                    requestType: null,
                    cronExpression: string.Empty,
                    JobPriority.LongRunning,
                    maxConcurrency: 1,
                    contractVersion: JobContract.InitialVersion
                ),
                _Options("shared-key"),
                ct
            );
            plain.Should().NotBe(facade, "a different function is a different reservation identity");

            // Same facade function + same key in system scope: a hit against the first facade call (payload is
            // not identity). Different scope: tenant-scoped must NOT dedup against the system-scoped rows.
            var system = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "s"),
                _Options("shared-key"),
                ct
            );
            system.Should().Be(facade, "same function + key + system scope dedups");
            var tenanted = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "t"),
                _Options("shared-key") with
                {
                    TenantId = "tenant-alpha",
                },
                ct
            );
            tenanted.Should().NotBe(facade, "tenant scope and system scope never share a reservation");
            tenanted.Should().NotBe(plain, "tenant scope and system scope never share a reservation");
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(3, "facade + plain + tenanted; system call was a hit");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task job_terminal_state_and_retention_delete_do_not_release_key()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync("idem-d", ct);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            var first = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "first"),
                _Options("terminal-key"),
                ct
            );

            // Terminalize then retention-delete the reserved job; the reservation must survive both.
            await manager.DeleteAsync(first, ct);
            var afterDelete = await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "after-delete"),
                _Options("terminal-key"),
                ct
            );
            afterDelete.Should().Be(first, "a deleted job does not release an unexpired reservation");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task coordinated_rollback_discards_reservation_and_job_together()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync("idem-e", ct);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                        (
                            await scheduler.EnqueueAsync(
                                new CoordinatedFacadeRequest(Guid.NewGuid(), "rolled-back"),
                                _Options("rollback-key"),
                                innerCt
                            )
                        )
                            .Should()
                            .NotBeEmpty();
                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);

            // The reservation must be gone with the job: the same key must open a fresh reservation now. The
            // fresh host shares the fixture database, so reset it before recreating the schema.
            await host.StopAsync(ct);
            await fixture.ResetDatabaseAsync(ct);
            using var fresh = fixture.BuildCoordinatedEnqueueHost("idem-e2");
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(fresh, ct);
            await fresh.StartAsync(ct);
            try
            {
                var scheduler2 = fresh.Services.GetRequiredService<IJobScheduler>();
                var recreated = await scheduler2.EnqueueAsync(
                    new CoordinatedFacadeRequest(Guid.NewGuid(), "recreated"),
                    _Options("rollback-key"),
                    ct
                );
                recreated.Should().NotBeEmpty();
                (await fixture.CountTimeJobsAsync(ct)).Should().Be(1, "only the recreated job exists");
            }
            finally
            {
                await fresh.StopAsync(ct);
            }
        }
        finally
        {
            // The rollback phase stops the first host explicitly before the fresh-host phase; stopping twice is
            // harmless (IHostAsyncLifetimeServiceProvider tolerates it), so keep the safety net.
            await host.StopAsync(ct);
        }
    }

    private async Task _BackdateReservationAsync(IHost host, string idempotencyKey, CancellationToken ct)
    {
        await using var db = await host
            .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(ct);
        var function = JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName;
        var reservation = await db.Set<JobIdempotencyReservationEntity>()
            .SingleAsync(row => row.IdempotencyKey == idempotencyKey && row.Function == function, ct);
        reservation.ExpiresAt = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        await db.SaveChangesAsync(ct);
    }
}
