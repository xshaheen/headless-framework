// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Cross-provider conformance for Jobs tenant propagation (U6): the tenant resolved or captured at schedule time is
/// persisted atomically with the row, survives the pickup projection after a lease timeout, and is resolved per node
/// across a chain. Proves the schedule-side capture (U2), the EF <c>TenantId</c> mapping + pickup projection (U4), and
/// the tenancy seam (U5) hold identically on every relational backend:
/// <list type="bullet">
/// <item>R9 — single and batch, direct and commit-coordinated enqueue persist the explicit tenant atomically.</item>
/// <item>AE1 — an ambient tenant is captured through the seam and persisted in the same write that created the row.</item>
/// <item>Regression guard — pickup after a lease timeout re-materializes <c>TenantId</c> through the EF projection
/// (the RetryCount-class silent-drop bug).</item>
/// <item>AE6 — chain descendants persist per-node tenants: an unset child inherits the root's resolved tenant, a
/// pre-set explicit child keeps its own.</item>
/// <item>Rollback — a coordinated enqueue carrying a tenant discards with the caller's transaction, leaving no row
/// (tenant capture has no post-commit step that could strand a direct-path row).</item>
/// </list>
/// Each leaf derives a sealed class with <c>[Collection&lt;TFixture&gt;]</c> and re-declares the methods with
/// <c>[Fact]</c> so the runner discovers them per provider.
/// </summary>
public abstract class JobsTenancyConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    // R9 (single, direct): a direct enqueue with an explicit entity tenant (R17 direct-entity-API parity) persists it.
    public virtual async Task direct_single_enqueue_persists_explicit_tenant()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var job = _TenantTimeJob("tenant-a");

            (await manager.AddAsync(job, ct)).Should().NotBeNull();

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
            (await fixture.ReadTimeJobTenantAsync(job.Id, ct)).Should().Be("tenant-a");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R9 (batch, direct): every row in a direct batch carries its own explicit tenant.
    public virtual async Task direct_batch_enqueue_persists_explicit_tenants()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var first = _TenantTimeJob("tenant-a");
            var second = _TenantTimeJob("tenant-b");

            (await manager.AddBatchAsync([first, second], ct)).Should().HaveCount(2);

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
            (await fixture.ReadTimeJobTenantAsync(first.Id, ct)).Should().Be("tenant-a");
            (await fixture.ReadTimeJobTenantAsync(second.Id, ct)).Should().Be("tenant-b");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R9 (single, coordinated): the tenant column commits inside the caller's transaction alongside the domain write.
    public virtual async Task coordinated_single_enqueue_persists_explicit_tenant_atomically()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var job = _TenantTimeJob("tenant-a");

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                    (await manager.AddAsync(job, innerCt)).Should().NotBeNull();
                },
                ct
            );

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
            (await fixture.ReadTimeJobTenantAsync(job.Id, ct)).Should().Be("tenant-a");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R9 (batch, coordinated): a batched enqueue commits every row's tenant with the caller's transaction.
    public virtual async Task coordinated_batch_enqueue_persists_explicit_tenants_atomically()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var first = _TenantTimeJob("tenant-a");
            var second = _TenantTimeJob("tenant-b");

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                    (await manager.AddBatchAsync([first, second], innerCt)).Should().HaveCount(2);
                },
                ct
            );

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
            (await fixture.ReadTimeJobTenantAsync(first.Id, ct)).Should().Be("tenant-a");
            (await fixture.ReadTimeJobTenantAsync(second.Id, ct)).Should().Be("tenant-b");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R9 via the IJobScheduler surface: EnqueueOptions.TenantId is copied onto the entity and persisted coordinated.
    public virtual async Task coordinated_scheduler_enqueue_persists_options_tenant()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var request = new CoordinatedFacadeRequest(Guid.NewGuid(), "tenant scheduler");
            var options = new EnqueueOptions { TenantId = "tenant-a" };
            var scheduledId = Guid.Empty;

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                    scheduledId = await scheduler.EnqueueAsync(request, options, innerCt);
                    scheduledId.Should().NotBeEmpty();
                },
                ct
            );

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
            (await fixture.ReadTimeJobTenantAsync(scheduledId, ct)).Should().Be("tenant-a");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE1: with the seam propagating and an ambient tenant set via ICurrentTenant.Change, an enqueue that supplies no
    // explicit tenant captures the ambient tenant and persists it in the same write — end-to-end capture proof.
    public virtual async Task coordinated_ambient_enqueue_captures_and_persists_tenant()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildTenantPropagationEnqueueHost("tenant-ambient-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await fixture.CreateProbeTableAsync(ct);
        await host.StartAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var currentTenant = host.Services.GetRequiredService<ICurrentTenant>();
            var job = _TenantTimeJob(tenantId: null);

            using (currentTenant.Change("tenant-ambient"))
            {
                await fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                        (await manager.AddAsync(job, innerCt)).Should().NotBeNull();
                    },
                    ct
                );
            }

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
            (await fixture.ReadTimeJobTenantAsync(job.Id, ct)).Should().Be("tenant-ambient");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // Regression guard (RetryCount-class): a timed-out row re-materialized through the EF pickup projection
    // (ForQueueTimeJobs) must carry TenantId, or a job restored after a lease timeout silently runs system scope.
    public virtual async Task pickup_after_lease_timeout_rematerializes_tenant()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("pickup-tenant-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "pickup-tenant",
                TenantId = "tenant-a",
                // Past execution time so the timed-out sweep claims it immediately.
                ExecutionTime = DateTime.UtcNow.AddMinutes(-5),
            };
            await persistence.AddTimeJobsAsync([job], ct);

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            claimed.Should().ContainSingle().Which.TenantId.Should().Be("tenant-a");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // AE6: a chain persists a per-node tenant — an unset descendant inherits the root's resolved tenant, a descendant
    // that carries its own explicit tenant keeps it. The chain walk lives in JobsManager (the middleware sees only the
    // BaseJobEntity root), so this is the integration-grade proof it runs before persistence on every backend.
    public virtual async Task chain_descendants_persist_resolved_tenants()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var inheritingChild = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                Request = [],
                RunCondition = RunCondition.OnSuccess,
            };
            var explicitChild = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                Request = [],
                TenantId = "tenant-b",
                RunCondition = RunCondition.OnSuccess,
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
                Request = [],
                TenantId = "tenant-a",
                // Far-future so the deferred immediate-dispatch branch never runs — keeps the assertion on row state.
                ExecutionTime = DateTime.UtcNow.AddHours(1),
                Children = [inheritingChild, explicitChild],
            };

            (await manager.AddAsync(root, ct)).Should().NotBeNull();

            (await fixture.ReadTimeJobTenantAsync(root.Id, ct)).Should().Be("tenant-a");
            (await fixture.ReadTimeJobTenantAsync(inheritingChild.Id, ct)).Should().Be("tenant-a");
            (await fixture.ReadTimeJobTenantAsync(explicitChild.Id, ct)).Should().Be("tenant-b");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // Rollback: a coordinated enqueue carrying a tenant discards with the caller's transaction — a stranded direct-path
    // capture would have auto-committed the row and left it behind, so both writes must be gone.
    public virtual async Task coordinated_rollback_with_tenant_leaves_no_row()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var job = _TenantTimeJob("tenant-a");
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                        await manager.AddAsync(job, innerCt);

                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // Regression guard: the generic update API must never clear a stored tenant — dashboard-style update payloads
    // omit TenantId, and a full-row update would silently downgrade the job to system scope on its next run.
    public virtual async Task update_preserves_stored_tenant_when_the_payload_omits_it()
    {
        var ct = AbortToken;
        using var host = await _StartCoordinatedHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var job = _TenantTimeJob("tenant-a");
            (await manager.AddAsync(job, ct)).Should().NotBeNull();

            job.TenantId = null; // dashboard-style payload omits the tenant
            job.Description = "edited";
            (await manager.UpdateAsync(job, ct)).Should().NotBeNull();

            (await fixture.ReadTimeJobTenantAsync(job.Id, ct)).Should().Be("tenant-a");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task<IHost> _StartCoordinatedHostAsync(CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);
        var host = fixture.BuildCoordinatedEnqueueHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, cancellationToken);
        await fixture.CreateProbeTableAsync(cancellationToken);
        await host.StartAsync(cancellationToken);

        return host;
    }

    private static TimeJobEntity _TenantTimeJob(string? tenantId)
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Request = [],
            TenantId = tenantId,
            // Far-future so the deferred immediate-dispatch branch never runs — keeps the assertion on row presence.
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };
    }
}
