// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Where a cron definition's recovery policy and grace come from, and — more importantly — what is allowed to change
/// them afterwards. The attribute declares an initial value; the persisted value is the authority.
/// </summary>
public sealed class CronRecoveryConfigurationTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@incarnation";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_seed_the_resolved_policy_and_grace_when_a_definition_is_created()
    {
        var provider = _Create();

        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Skip, 300)],
            AbortToken
        );

        var definition = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();
        definition.OnMissedRun.Should().Be(MissedRunPolicy.Skip);
        definition.MissedRunGraceSeconds.Should().Be(300);
    }

    /// <summary>
    /// AE16: a runtime override survives application restart and attribute reconciliation.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the attribute seeds at creation only. If reconciliation reapplied it, every redeploy
    /// would silently revert an operator's decision — and the system would then need a provenance marker to tell an
    /// override from a default, which is exactly the complexity this rule avoids.
    /// </remarks>
    [Fact]
    public async Task should_not_let_startup_reconciliation_overwrite_a_runtime_override()
    {
        var provider = _Create();

        // Boot 1: the attribute seeds Coalesce/60.
        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );
        var created = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();

        // An operator overrides the policy at runtime.
        var overridden = _Clone(created);
        overridden.OnMissedRun = MissedRunPolicy.Skip;
        overridden.MissedRunGraceSeconds = 900;
        var updated = await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(overridden, created.ScheduleRevision, NextOccurrenceFactory: null)],
            _Now,
            AbortToken
        );
        updated.Should().NotBeNull();

        // Boot 2: the application restarts and reconciles the same declared function, attribute values unchanged.
        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );

        var afterRestart = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();
        afterRestart
            .OnMissedRun.Should()
            .Be(MissedRunPolicy.Skip, "the attribute seeds at creation only and must never be reapplied");
        afterRestart.MissedRunGraceSeconds.Should().Be(900);
    }

    [Fact]
    public async Task should_preserve_a_runtime_override_even_when_the_expression_changes()
    {
        var provider = _Create();
        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded", "0 * * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );
        var created = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();

        var overridden = _Clone(created);
        overridden.OnMissedRun = MissedRunPolicy.Skip;
        await provider.UpdateCronJobsAtomicallyAsync(
            [new CronJobAtomicUpdate<FakeCronJob>(overridden, created.ScheduleRevision, NextOccurrenceFactory: null)],
            _Now,
            AbortToken
        );

        // A code change alters the declared expression — the reconciliation path that DOES mutate the row.
        await provider.MigrateDefinedCronJobsAsync(
            [new CronSeedDefinition("seeded", "0 */5 * * * *", MissedRunPolicy.Coalesce, 60)],
            AbortToken
        );

        var afterChange = (await provider.GetCronJobsAsync(predicate: null, AbortToken)).Single();
        afterChange.Expression.Should().Be("0 */5 * * * *", "the expression edit must still apply");
        afterChange
            .OnMissedRun.Should()
            .Be(MissedRunPolicy.Skip, "an expression change is not licence to revert an unrelated operator override");
    }

    [Fact]
    public void the_attribute_reports_framework_defaults_when_left_unset()
    {
        var attribute = new JobFunctionAttribute("f", "0 * * * * *");

        attribute.OnMissedRun.Should().Be(MissedRunPolicy.Coalesce);
        attribute.MissedRunGraceSeconds.Should().Be(JobsRecoveryDefaults.MissedRunGraceSeconds);
    }

    [Fact]
    public void the_attribute_round_trips_explicit_values()
    {
        var attribute = new JobFunctionAttribute("f", "0 * * * * *")
        {
            OnMissedRun = MissedRunPolicy.Skip,
            MissedRunGraceSeconds = 120,
        };

        attribute.OnMissedRun.Should().Be(MissedRunPolicy.Skip);
        attribute.MissedRunGraceSeconds.Should().Be(120);
    }

    /// <summary>
    /// The registration is baked into every consumer assembly's ModuleInitializer, so a consumer compiled before these
    /// knobs existed must still register. Unset reads as null and falls through to the scheduler-wide default.
    /// </summary>
    [Fact]
    public void the_registration_treats_omitted_recovery_knobs_as_unset()
    {
        var registration = new JobFunctionRegistration
        {
            CronExpression = "0 * * * * *",
            Priority = JobPriority.Normal,
            Delegate = (_, _, _) => Task.CompletedTask,
            MaxConcurrency = 0,
        };

        registration.OnMissedRun.Should().BeNull();
        registration.MissedRunGraceSeconds.Should().BeNull();
    }

    private static FakeCronJob _Clone(FakeCronJob source) =>
        new()
        {
            Id = source.Id,
            Function = source.Function,
            Expression = source.Expression,
            TimeZoneId = source.TimeZoneId,
            IsPaused = source.IsPaused,
            ScheduleRevision = source.ScheduleRevision,
            ReconciledThroughUtc = source.ReconciledThroughUtc,
            NextDueUtc = source.NextDueUtc,
            OnMissedRun = source.OnMissedRun,
            MissedRunGraceSeconds = source.MissedRunGraceSeconds,
            Retries = source.Retries,
            RetryIntervals = source.RetryIntervals,
            OnNodeDeath = source.OnNodeDeath,
            Request = source.Request,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
        };

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _Owner });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }
}
