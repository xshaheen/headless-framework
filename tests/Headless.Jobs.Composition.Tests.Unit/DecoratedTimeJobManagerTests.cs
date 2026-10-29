// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A consumer may decorate the public <see cref="ITimeJobManager{TTimeJob}"/>; the scheduler must reach the delayed
/// enqueue through that public contract, so a decorator never breaks <c>ScheduleAfterAsync</c>.
/// </summary>
public sealed class DecoratedTimeJobManagerTests : TestBase
{
    private const string _Function = "decorated-delay";

    [Fact]
    public async Task should_schedule_after_a_delay_through_a_decorated_time_job_manager()
    {
        var (provider, scheduler, decorator, clock) = _Host();
        await using var host = provider;
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var delay = TimeSpan.FromMinutes(7);

        var id = await scheduler.ScheduleAfterAsync(new DelayRequest("plain"), delay, AbortToken);

        decorator.Calls.Should().ContainSingle().Which.Should().Be($"AddAsync(delay={delay})");
        var stored = await persistence.GetTimeJobByIdAsync(id, AbortToken);
        stored.Should().NotBeNull();
        stored!.ExecutionTime.Should().Be(clock.GetUtcNow().UtcDateTime + delay);
    }

    [Fact]
    public async Task should_schedule_an_idempotent_job_after_a_delay_through_a_decorated_time_job_manager()
    {
        var (provider, scheduler, decorator, clock) = _Host();
        await using var host = provider;
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var delay = TimeSpan.FromMinutes(3);
        var options = new JobOptions { IdempotencyKey = "decorated-key", IdempotencyTtl = TimeSpan.FromHours(1) };

        var id = await scheduler.ScheduleAfterAsync(new DelayRequest("idempotent"), delay, options, AbortToken);

        decorator.Calls.Should().ContainSingle().Which.Should().Be($"AddIdempotentAsync(decorated-key, delay={delay})");
        var stored = await persistence.GetTimeJobByIdAsync(id, AbortToken);
        stored.Should().NotBeNull();
        stored!.ExecutionTime.Should().Be(clock.GetUtcNow().UtcDateTime + delay);
    }

    [Fact]
    public async Task should_reject_a_negative_delay_or_a_preset_execution_time_on_the_public_delayed_members()
    {
        var (provider, _, decorator, clock) = _Host();
        await using var host = provider;
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        ITimeJobManager<TimeJobEntity> manager = decorator;

        Action negative = () => _ = manager.AddAsync(_Entity(), TimeSpan.FromTicks(-1), AbortToken);
        Action negativeIdempotent = () =>
            _ = manager.AddIdempotentAsync(_Entity(), "k", TimeSpan.FromHours(1), TimeSpan.FromTicks(-1), AbortToken);
        var preset = _Entity();
        preset.ExecutionTime = clock.GetUtcNow().UtcDateTime.AddHours(1);
        Action presetTime = () => _ = manager.AddAsync(preset, TimeSpan.FromMinutes(1), AbortToken);
        Action presetIdempotent = () =>
            _ = manager.AddIdempotentAsync(preset, "k", TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), AbortToken);

        // Synchronous: the bad call fails before the returned task exists, so nothing is written.
        negative.Should().Throw<ArgumentOutOfRangeException>();
        negativeIdempotent.Should().Throw<ArgumentOutOfRangeException>();
        presetTime.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("entity");
        presetIdempotent.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("entity");
        (await persistence.GetEarliestTimeJobsAsync(AbortToken)).Jobs.Should().BeEmpty();
    }

    private static TimeJobEntity _Entity() => new() { Function = _Function };

    private static (
        ServiceProvider Provider,
        IJobScheduler Scheduler,
        RecordingTimeJobManager Decorator,
        FakeTimeProvider Clock
    ) _Host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        var descriptor = new JobFunctionDescriptor(_Function, typeof(DelayRequest), "", JobPriority.Normal, 0, "v1");
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
                        (typeof(DelayRequest).FullName!, typeof(DelayRequest))
                    ),
                ],
                [new KeyValuePair<string, JobFunctionDescriptor>(_Function, descriptor)]
            )
        );
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-01T08:00:00Z", CultureInfo.InvariantCulture));
        services.AddSingleton<TimeProvider>(clock);

        // Wrap the framework's registration the way a consumer decorator would: the scheduler then only ever sees the
        // public interface, never the framework's own implementation type.
        var original = services.Single(service => service.ServiceType == typeof(ITimeJobManager<TimeJobEntity>));
        services.Remove(original);
        services.AddSingleton(provider => new RecordingTimeJobManager(
            (ITimeJobManager<TimeJobEntity>)original.ImplementationFactory!(provider)
        ));
        services.AddSingleton<ITimeJobManager<TimeJobEntity>>(provider =>
            provider.GetRequiredService<RecordingTimeJobManager>()
        );

        var serviceProvider = services.BuildServiceProvider();
        return (
            serviceProvider,
            serviceProvider.GetRequiredService<IJobScheduler>(),
            serviceProvider.GetRequiredService<RecordingTimeJobManager>(),
            clock
        );
    }

    private sealed record DelayRequest(string Value);

    private sealed class RecordingTimeJobManager(ITimeJobManager<TimeJobEntity> inner) : ITimeJobManager<TimeJobEntity>
    {
        public List<string> Calls { get; } = [];

        public Task<JobScheduleResult> ScheduleKeyedAsync(
            JobKey key,
            TimeJobEntity entity,
            long? expectedGeneration = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("ScheduleKeyedAsync");
            return inner.ScheduleKeyedAsync(key, entity, expectedGeneration, cancellationToken);
        }

        public Task<JobScheduleResult> CancelKeyedAsync(
            JobKeyScope scope,
            JobKey key,
            long expectedGeneration,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("CancelKeyedAsync");
            return inner.CancelKeyedAsync(scope, key, expectedGeneration, cancellationToken);
        }

        public Task<TimeJobEntity> AddAsync(TimeJobEntity entity, CancellationToken cancellationToken = default)
        {
            Calls.Add("AddAsync");
            return inner.AddAsync(entity, cancellationToken);
        }

        public Task<TimeJobEntity> AddIdempotentAsync(
            TimeJobEntity entity,
            string idempotencyKey,
            TimeSpan idempotencyTtl,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add($"AddIdempotentAsync({idempotencyKey})");
            return inner.AddIdempotentAsync(entity, idempotencyKey, idempotencyTtl, cancellationToken);
        }

        public Task<TimeJobEntity> AddAsync(
            TimeJobEntity entity,
            TimeSpan delay,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add($"AddAsync(delay={delay})");
            return inner.AddAsync(entity, delay, cancellationToken);
        }

        public Task<TimeJobEntity> AddIdempotentAsync(
            TimeJobEntity entity,
            string idempotencyKey,
            TimeSpan idempotencyTtl,
            TimeSpan delay,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add($"AddIdempotentAsync({idempotencyKey}, delay={delay})");
            return inner.AddIdempotentAsync(entity, idempotencyKey, idempotencyTtl, delay, cancellationToken);
        }

        public Task<JobResult<TimeJobEntity>> UpdateAsync(
            TimeJobEntity timeJob,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("UpdateAsync");
            return inner.UpdateAsync(timeJob, cancellationToken);
        }

        public Task<JobResult<TimeJobEntity>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Calls.Add("DeleteAsync");
            return inner.DeleteAsync(id, cancellationToken);
        }

        public Task<List<TimeJobEntity>> AddBatchAsync(
            List<TimeJobEntity> entities,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("AddBatchAsync");
            return inner.AddBatchAsync(entities, cancellationToken);
        }

        public Task<JobResult<List<TimeJobEntity>>> UpdateBatchAsync(
            List<TimeJobEntity> timeJobs,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("UpdateBatchAsync");
            return inner.UpdateBatchAsync(timeJobs, cancellationToken);
        }

        public Task<JobResult<TimeJobEntity>> DeleteBatchAsync(
            List<Guid> ids,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add("DeleteBatchAsync");
            return inner.DeleteBatchAsync(ids, cancellationToken);
        }
    }
}
