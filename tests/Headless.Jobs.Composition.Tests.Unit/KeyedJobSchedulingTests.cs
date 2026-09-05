// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class KeyedJobSchedulingTests : TestBase
{
    [Fact]
    public async Task in_memory_executes_the_shared_provider_matrix()
    {
        await using var provider = _Services().BuildServiceProvider();
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await JobsKeyedSchedulingScenarios.RunAsync(store, AbortToken);
        await using var model = new ParentMappingContext(
            new DbContextOptionsBuilder<ParentMappingContext>().UseSqlite("Data Source=:memory:").Options
        );
        await JobsKeyedSchedulingScenarios.RunParentAttachmentRejectionsAsync(
            store,
            provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>(),
            (job, parentId) => model.Entry(job).Property(row => row.ParentId).CurrentValue = parentId,
            AbortToken
        );
        var futureParent = JobsKeyedSchedulingScenarios.Candidate();
        var orphan = JobsKeyedSchedulingScenarios.Candidate();
        model.Entry(orphan).Property(row => row.ParentId).CurrentValue = futureParent.Id;
        await store.AddTimeJobsAsync([orphan], AbortToken);
        var adoptChildren = async () =>
            await store.ScheduleKeyedTimeJobAsync(
                new JobKey("future-parent"),
                futureParent,
                cancellationToken: AbortToken
            );
        await adoptChildren.Should().ThrowAsync<NotSupportedException>().WithMessage("*JobChain*");
        (await store.GetTimeJobByIdAsync(futureParent.Id, AbortToken)).Should().BeNull();
        (await store.GetTimeJobByIdAsync(orphan.Id, AbortToken)).Should().NotBeNull();
        await JobsKeyedSchedulingScenarios.RunClaimRacesAsync(
            store,
            async candidate =>
                (await store.QueueTimeJobsAsync([candidate], AbortToken).ToArrayAsync(AbortToken)).Length == 1,
            AbortToken
        );
        await JobsKeyedSchedulingScenarios.RunLegacyMutationRacesAsync(store, AbortToken);
    }

    private sealed class ParentMappingContext(DbContextOptions<ParentMappingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TimeJobEntity>());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("payload")]
    [InlineData("due")]
    [InlineData("retries")]
    [InlineData("intervals")]
    [InlineData("node-death")]
    public void fingerprint_changes_only_for_behavioral_intent(string changed)
    {
        var original = JobsKeyedSchedulingScenarios.Candidate();
        var modified = JobsKeyedSchedulingScenarios.Candidate();
        switch (changed)
        {
            case "version":
                modified.ContractVersion = "2";
                break;
            case "payload":
                modified.Request = [1, 2, 4];
                break;
            case "due":
                modified.ExecutionTime = modified.ExecutionTime!.Value.AddTicks(10);
                break;
            case "retries":
                modified.Retries = 1;
                break;
            case "intervals":
                modified.RetryIntervals = [1];
                break;
            case "node-death":
                modified.OnNodeDeath = NodeDeathPolicy.Skip;
                break;
        }

        JobIntentFingerprint.Normalize(original);
        JobIntentFingerprint.Normalize(modified);
        JobIntentFingerprint.Compute(original, "v1").Should().NotBe(JobIntentFingerprint.Compute(modified, "v1"));
    }

    [Fact]
    public void canonical_fingerprint_preserves_exact_bytes_and_common_time_precision()
    {
        var one = JobsKeyedSchedulingScenarios.Candidate();
        var two = JobsKeyedSchedulingScenarios.Candidate();
        two.ExecutionTime = new DateTimeOffset(2030, 1, 1, 5, 30, 0, TimeSpan.FromMinutes(330)).UtcDateTime.AddTicks(9);
        two.RetryIntervals = [];
        two.TenantId = "other-tenant";
        two.CorrelationId = "other-root";
        two.Description = "other-display";
        JobIntentFingerprint.Normalize(one);
        JobIntentFingerprint.Normalize(two);
        JobIntentFingerprint
            .Compute(one, "v1")
            .Should()
            .Be("caa4a313cae19b0fe80623b8f440a4c20bd7749c2384aa61e9019770fdcca86f");
        one.ExecutionTime.Should().Be(two.ExecutionTime);
        JobIntentFingerprint.Compute(one, "v1").Should().Be(JobIntentFingerprint.Compute(two, "v1"));
        one.Request = null;
        two.Request = [];
        JobIntentFingerprint.Compute(one, "v1").Should().NotBe(JobIntentFingerprint.Compute(two, "v1"));
        var unknown = () => JobIntentFingerprint.Compute(one, "v99");
        unknown.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task scheduler_normalizes_absolute_offsets_and_exposes_generation_fences()
    {
        var descriptor = new JobFunctionDescriptor("deadline", null, "", JobPriority.Normal, 0);
        var services = _Services();
        services.AddSingleton(
            JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        "deadline",
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, _, _) => Task.CompletedTask,
                        }
                    ),
                ],
                [],
                [new KeyValuePair<string, JobFunctionDescriptor>("deadline", descriptor)]
            )
        );
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var key = new JobKey("facade");
        var due = new DateTimeOffset(2030, 1, 1, 5, 30, 0, TimeSpan.FromMinutes(330)).AddTicks(9);
        var created = await scheduler.ScheduleKeyedAsync(key, descriptor, due, cancellationToken: AbortToken);
        var observed = await scheduler.ScheduleKeyedAsync(
            key,
            descriptor,
            due.ToUniversalTime(),
            new EnqueueOptions { Description = "new description" },
            AbortToken
        );
        observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
        observed.RunId.Should().Be(created.RunId);
        var replaced = await scheduler.ReplaceKeyedAsync(
            key,
            1,
            descriptor,
            due.AddMinutes(1),
            cancellationToken: AbortToken
        );
        replaced.Disposition.Should().Be(JobScheduleDisposition.Replaced);
        (await scheduler.CancelKeyedAsync(new JobKeyScope("deadline"), key, 1, AbortToken))
            .Disposition.Should()
            .Be(JobScheduleDisposition.StaleGeneration);
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var persisted = await store.GetTimeJobByIdAsync(replaced.RunId!.Value, AbortToken);
        persisted!.ExecutionTime!.Value.Ticks.Should().Be(due.AddMinutes(1).UtcTicks / 10 * 10);
        var dto = JobsKeyedSchedulingScenarios.Candidate([9]);
        dto.Id = persisted.Id;
        var manager = provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var update = await manager.UpdateAsync(dto, AbortToken);
        update.IsSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task keyed_chains_are_rejected_before_the_manager()
    {
        await using var provider = _Services().BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var chain = JobChain.Start(new ChainRequest()).Build();
        var schedule = async () =>
            await scheduler.ScheduleKeyedAsync(
                new JobKey("chain"),
                chain,
                new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                cancellationToken: AbortToken
            );
        await schedule.Should().ThrowAsync<NotSupportedException>().WithMessage("*JobChain*");
        var replace = async () =>
            await scheduler.ReplaceKeyedAsync(
                new JobKey("chain"),
                1,
                chain,
                new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                cancellationToken: AbortToken
            );
        await replace.Should().ThrowAsync<NotSupportedException>().WithMessage("*JobChain*");
    }

    [Fact]
    public async Task schedule_policy_changes_are_fingerprinted_after_the_pipeline()
    {
        JobFunctionProvider.ResetForTests(discoveryComplete: false);
        var retryPolicy = 2;
        JobMiddlewareRegistry.RegisterSchedule(
            "keyed-policy",
            "deadline",
            0,
            (context, next, cancellationToken) =>
            {
                var row = (TimeJobEntity)context.Job;
                row.Retries = retryPolicy;
                row.ExecutionTime = row.ExecutionTime!.Value.AddMinutes(1).AddTicks(1);
                return next(cancellationToken);
            }
        );
        try
        {
            var descriptor = new JobFunctionDescriptor("deadline", null, "", JobPriority.Normal, 0);
            var services = _Services();
            services.AddSingleton(
                JobFunctionRegistryBuilder.Build(
                    [
                        new KeyValuePair<string, JobFunctionRegistration>(
                            "deadline",
                            new()
                            {
                                CronExpression = "",
                                Priority = JobPriority.Normal,
                                MaxConcurrency = 0,
                                Delegate = (_, _, _) => Task.CompletedTask,
                            }
                        ),
                    ],
                    [],
                    [new KeyValuePair<string, JobFunctionDescriptor>("deadline", descriptor)]
                )
            );
            await using var provider = services.BuildServiceProvider();
            var scheduler = provider.GetRequiredService<IJobScheduler>();
            var key = new JobKey("policy");
            var due = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(9);
            var created = await scheduler.ScheduleKeyedAsync(key, descriptor, due, cancellationToken: AbortToken);
            var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var persisted = await store.GetTimeJobByIdAsync(created.RunId!.Value, AbortToken);
            persisted!.Retries.Should().Be(2);
            persisted.ExecutionTime.Should().Be(due.AddMinutes(1).AddTicks(1).UtcDateTime);
            (await scheduler.ScheduleKeyedAsync(key, descriptor, due, cancellationToken: AbortToken))
                .Disposition.Should()
                .Be(JobScheduleDisposition.Existing);
            retryPolicy = 3;
            (await scheduler.ScheduleKeyedAsync(key, descriptor, due, cancellationToken: AbortToken))
                .Disposition.Should()
                .Be(JobScheduleDisposition.Conflict);
        }
        finally
        {
            JobFunctionProvider.ResetForTests();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("padded ")]
    [InlineData("bad\nkey")]
    public void invalid_identity_is_rejected_without_lossy_normalization(string value)
    {
        var key = () => new JobKey(value);
        key.Should().Throw<ArgumentException>();
        var scope = () => new JobKeyScope("deadline", value);
        scope.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void identity_rejects_invalid_unicode_and_bounds_utf16_units()
    {
        var invalid = () => new JobKey(new string('\ud800', 1));
        invalid.Should().Throw<ArgumentException>();
        var oversized = () => new JobKey(new string('x', 201));
        oversized.Should().Throw<ArgumentException>();
        new JobKey(new string('x', 200)).Value.Should().HaveLength(200);
    }

    private sealed record ChainRequest;

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        return services;
    }

    [Fact]
    public async Task concurrent_identical_schedules_share_one_retained_generation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider();
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var jobs = Enumerable
            .Range(0, 8)
            .Select(_ => new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "deadline",
                ContractVersion = "1",
                ExecutionTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Request = [1, 2, 3],
            });

        var results = await Task.WhenAll(
            jobs.Select(job =>
                persistence.ScheduleKeyedTimeJobAsync(
                    new JobKey("invoice-42"),
                    job,
                    expectedGeneration: null,
                    AbortToken
                )
            )
        );

        results.Count(result => result.Disposition == JobScheduleDisposition.Created).Should().Be(1);
        results.Count(result => result.Disposition == JobScheduleDisposition.Existing).Should().Be(7);
        results.Select(result => result.RunId).Distinct().Should().ContainSingle();
        results.Should().OnlyContain(result => result.Generation == 1);
    }
}
