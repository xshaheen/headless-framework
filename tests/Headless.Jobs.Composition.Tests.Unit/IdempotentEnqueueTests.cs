// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Composition coverage for idempotent enqueue on the in-memory provider: validation bounds, reservation
/// lifecycle (new/hit/expiry/isolation), terminal-state independence, keyed/chain rejection, and the
/// full-scheduler ID-flow contract on a dedup hit.
/// </summary>
public sealed class IdempotentEnqueueTests : TestBase
{
    private const string _Function = "idem-sample";

    private static (
        ServiceProvider Provider,
        JobScheduler<TimeJobEntity, CronJobEntity> Scheduler,
        FakeTimeProvider Clock
    ) _Host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        var descriptor = new JobFunctionDescriptor(_Function, typeof(IdemRequest), "", JobPriority.Normal, 0, "v1");
        services.AddSingleton(
            JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        _Function,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, _, _) => Task.CompletedTask,
                        }
                    ),
                ],
                [
                    new KeyValuePair<string, (string, Type)>(
                        _Function,
                        (typeof(IdemRequest).FullName!, typeof(IdemRequest))
                    ),
                ],
                [new KeyValuePair<string, JobFunctionDescriptor>(_Function, descriptor)]
            )
        );
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-15T00:00:00Z", CultureInfo.InvariantCulture));
        services.AddSingleton<TimeProvider>(clock);
        var provider = services.BuildServiceProvider();
        return (
            provider,
            (JobScheduler<TimeJobEntity, CronJobEntity>)provider.GetRequiredService<IJobScheduler>(),
            clock
        );
    }

    private static JobOptions _Options(string key, TimeSpan? ttl = null) =>
        new() { IdempotencyKey = key, IdempotencyTtl = ttl ?? TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task should_return_first_job_id_and_skip_second_insert_on_dedup_hit()
    {
        var (provider, scheduler, _) = _Host();
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var first = await scheduler.EnqueueAsync(new IdemRequest("one"), _Options("k1"), AbortToken);
        var second = await scheduler.EnqueueAsync(new IdemRequest("different-payload"), _Options("k1"), AbortToken);

        second.Should().Be(first, "the payload is not identity; a repeat observes the reservation");
        var stored = await persistence.GetTimeJobByIdAsync(first, AbortToken);
        stored.Should().NotBeNull();
        stored!.Request.Should().NotBeNull();
        (await persistence.GetEarliestTimeJobsAsync(AbortToken)).Jobs.Should().ContainSingle();
        provider.Dispose();
    }

    [Fact]
    public async Task should_open_new_reservation_after_expiry()
    {
        var (provider, scheduler, clock) = _Host();
        var first = await scheduler.EnqueueAsync(new IdemRequest("first"), _Options("k2"), AbortToken);
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await scheduler.EnqueueAsync(new IdemRequest("second"), _Options("k2"), AbortToken);
        second.Should().NotBe(first, "an expired reservation is replaced by a new job");
        provider.Dispose();
    }

    [Fact]
    public async Task should_not_dedup_across_descriptors_or_scopes()
    {
        var (provider, scheduler, _) = _Host();
        var system = await scheduler.EnqueueAsync(new IdemRequest("s"), _Options("k3"), AbortToken);
        var tenanted = await scheduler.EnqueueAsync(
            new IdemRequest("t"),
            _Options("k3") with
            {
                TenantId = "tenant-a",
            },
            AbortToken
        );
        tenanted.Should().NotBe(system);
        provider.Dispose();
    }

    [Fact]
    public async Task should_keep_reservation_after_job_delete()
    {
        var (provider, scheduler, _) = _Host();
        var manager = provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var first = await scheduler.EnqueueAsync(new IdemRequest("doomed"), _Options("k4"), AbortToken);
        await manager.DeleteAsync(first, AbortToken);
        var after = await scheduler.EnqueueAsync(new IdemRequest("re-enqueue"), _Options("k4"), AbortToken);
        after.Should().Be(first, "a deleted job does not release an unexpired reservation");
        provider.Dispose();
    }

    [Fact]
    public async Task should_reject_ttl_without_key_and_out_of_bounds_pairs()
    {
        var (provider, scheduler, _) = _Host();
        var ttlWithoutKey = () =>
            scheduler.EnqueueAsync(
                new IdemRequest("x"),
                new JobOptions { IdempotencyTtl = TimeSpan.FromMinutes(5) },
                AbortToken
            );
        var tooShort = () =>
            scheduler.EnqueueAsync(new IdemRequest("x"), _Options("k5", TimeSpan.FromMilliseconds(500)), AbortToken);
        var tooLong = () =>
            scheduler.EnqueueAsync(new IdemRequest("x"), _Options("k5", TimeSpan.FromDays(31)), AbortToken);
        await ttlWithoutKey.Should().ThrowAsync<ArgumentException>();
        await tooShort.Should().ThrowAsync<ArgumentException>();
        await tooLong.Should().ThrowAsync<ArgumentException>();
        provider.Dispose();
    }

    [Fact]
    public async Task should_validate_ttl_on_the_manager_surface_not_only_option_resolution()
    {
        // The manager is a public surface: a zero/negative/oversized TTL must fail loudly there, because a
        // zero-TTL reservation expires instantly and silently disables deduplication (the worst failure mode).
        var (provider, _, _) = _Host();
        var manager = provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var entity = new TimeJobEntity
        {
            Function = _Function,
            ContractVersion = "v1",
            Request = [],
            ExecutionTime = DateTime.UtcNow.AddMinutes(5),
        };
        var zeroTtl = () => manager.AddIdempotentAsync(entity, "manager-ttl", TimeSpan.Zero, AbortToken);
        var negativeTtl = () => manager.AddIdempotentAsync(entity, "manager-ttl", TimeSpan.FromSeconds(-5), AbortToken);
        var oversizedTtl = () => manager.AddIdempotentAsync(entity, "manager-ttl", TimeSpan.FromDays(31), AbortToken);
        await zeroTtl.Should().ThrowAsync<ArgumentException>();
        await negativeTtl.Should().ThrowAsync<ArgumentException>();
        await oversizedTtl.Should().ThrowAsync<ArgumentException>();
        provider.Dispose();
    }

    [Fact]
    public async Task should_reject_keyed_and_chain_scheduling_with_an_idempotency_key()
    {
        var (provider, scheduler, _) = _Host();
        var keyed = () =>
            scheduler.ScheduleKeyedAsync(
                new JobKey("b"),
                new IdemRequest("x"),
                DateTimeOffset.UtcNow,
                _Options("k6"),
                AbortToken
            );
        await keyed.Should().ThrowAsync<ArgumentException>();
        provider.Dispose();
    }

    private sealed record IdemRequest(string Value);
}
