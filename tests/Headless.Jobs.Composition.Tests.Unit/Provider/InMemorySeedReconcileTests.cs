// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store reconciles one owner's seeded cron definitions the way the relational store does: matched by
/// id, deleting what the owner no longer declares, and never touching runtime rows or another owner's rows.
/// </summary>
public sealed class InMemorySeedReconcileTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "billing-app";
    private const string _OtherOwner = "reports-app";
    private const string _Function = "seed.cleanup";
    private const string _SeededExpression = "0 */5 * * * *";
    private const string _RuntimeExpression = "0 0 * * * *";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_delete_a_seeded_row_and_its_occurrences_when_the_next_seed_no_longer_declares_it()
    {
        var provider = _Create();
        await _SeedAsync(provider, _Owner, [_Function, "seed.kept"]);
        var removed = (await provider.GetCronJobsAsync(x => x.Function == _Function, AbortToken)).Single();
        await provider.InsertCronJobOccurrencesAsync(
            [
                new CronJobOccurrenceEntity<FakeCronJob>
                {
                    Id = Guid.NewGuid(),
                    CronJobId = removed.Id,
                    ExecutionTime = _Now.UtcDateTime.AddMinutes(5),
                    Status = JobStatus.Idle,
                    CreatedAt = _Now,
                    UpdatedAt = _Now,
                },
            ],
            AbortToken
        );

        await _SeedAsync(provider, _Owner, ["seed.kept"]);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Select(x => x.Function).Should().Equal("seed.kept");
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == removed.Id, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_another_owners_seeded_row_when_this_owner_removes_the_same_function()
    {
        var provider = _Create();
        await _SeedAsync(provider, _OtherOwner, [_Function]);
        await _SeedAsync(provider, _Owner, [_Function]);

        await _SeedAsync(provider, _Owner, []);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Should().ContainSingle().Which.Function.Should().Be(_Function);
    }

    [Fact]
    public async Task should_leave_a_runtime_row_of_an_attributed_function_unchanged()
    {
        var provider = _Create();
        var runtime = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Expression = _RuntimeExpression,
            CreatedAt = _Now,
            UpdatedAt = _Now,
            Request = [],
        };
        await provider.InsertCronJobsAsync([runtime], AbortToken);

        await _SeedAsync(provider, _Owner, [_Function]);
        await _SeedAsync(provider, _Owner, []);
        await _SeedAsync(provider, _Owner, [_Function]);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        var stored = rows.Should().ContainSingle(x => x.Id == runtime.Id).Subject;
        stored.Expression.Should().Be(_RuntimeExpression);
        stored.InitIdentifier.Should().BeNull();
        rows.Should().ContainSingle(x => x.Id != runtime.Id).Which.Expression.Should().Be(_SeededExpression);
    }

    private static Task _SeedAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        string owner,
        string[] functions
    )
    {
        return provider.MigrateDefinedCronJobsAsync(
            owner,
            [
                .. functions.Select(function =>
                    CronSeedDefinition.ForCronFunction(
                        owner,
                        function,
                        _SeededExpression,
                        MissedRunPolicy.Coalesce,
                        JobsRecoveryDefaults.MissedRunGraceSeconds,
                        CronOverlapPolicy.Allow
                    )
                ),
            ],
            AbortToken
        );
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a@incarnation" });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }
}
