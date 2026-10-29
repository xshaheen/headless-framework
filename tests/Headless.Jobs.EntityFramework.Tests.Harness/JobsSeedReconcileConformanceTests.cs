// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The seed reconciles the cron definitions one owner declared, matched by their deterministic ids. A row created at
/// runtime, or seeded by another owner sharing the store, is never adopted, rewritten, or deleted.
/// </summary>
public abstract class JobsSeedReconcileConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    // The two owners differ only where the "_" sits, so a prefix match that left "_" as a LIKE wildcard would treat
    // the other owner's rows as this owner's.
    private const string _Owner = "billing_app";
    private const string _OtherOwner = "billing-app";
    private const string _Function = "seed.cleanup";
    private const string _SeededExpression = "0 */5 * * * *";
    private const string _RuntimeExpression = "0 0 * * * *";

    public virtual async Task seed_leaves_a_runtime_row_of_an_attributed_function_unchanged()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-runtime-row");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var runtimeId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(runtimeId, _Function, _RuntimeExpression, NodeDeathPolicy.Retry, ct);

        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression)], ct);
        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression)], ct);

        var rows = await persistence.GetCronJobsAsync(x => x.Function == _Function, ct);
        var runtime = rows.Should().ContainSingle(x => x.Id == runtimeId).Subject;
        runtime.Expression.Should().Be(_RuntimeExpression, "the seed must not adopt a row it did not create");
        runtime.ScheduleRevision.Should().Be(0);
        runtime.InitIdentifier.Should().BeNull();
        var seeded = rows.Should().ContainSingle(x => x.Id != runtimeId).Subject;
        seeded.Expression.Should().Be(_SeededExpression, "the declared function still gets its own seeded row");
        seeded.InitIdentifier.Should().Be("headless.jobs.seed:billing_app:cron:seed.cleanup");
    }

    public virtual async Task removing_a_function_deletes_only_this_owners_seeded_row()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-owner-scope");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);

        await _SeedAsync(persistence, _OtherOwner, [(_Function, _SeededExpression)], ct);
        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression), ("seed.kept", _SeededExpression)], ct);
        await _SeedAsync(persistence, _Owner, [("seed.kept", _SeededExpression)], ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Select(x => x.Function)
            .Should()
            .BeEquivalentTo(
                [_Function, "seed.kept"],
                "the other owner's row for the removed function survives and this owner's own row is gone"
            );
    }

    public virtual async Task concurrent_seeds_of_one_set_leave_one_row_per_function()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("seed-converge-a");
        using var secondHost = fixture.BuildHost("seed-converge-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        var first = _Persistence(firstHost);
        var second = _Persistence(secondHost);
        (string, string)[] declared =
        [
            ("seed.alpha", _SeededExpression),
            ("seed.beta", _SeededExpression),
            ("seed.gamma", _SeededExpression),
        ];

        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(_SeedAsync(first, _Owner, declared, ct), _SeedAsync(second, _Owner, declared, ct));
        }

        var rows = await first.GetCronJobsAsync(predicate: null, ct);
        rows.Select(x => x.Function).Should().BeEquivalentTo(["seed.alpha", "seed.beta", "seed.gamma"]);
    }

    public virtual async Task seed_restores_the_expression_of_a_paused_row_and_keeps_it_paused()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-paused-row");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        await _SeedAsync(persistence, _Owner, [(_Function, _RuntimeExpression)], ct);
        var created = (await persistence.GetCronJobsAsync(predicate: null, ct)).Single();
        (await persistence.PauseCronJobAsync(created.Id, DateTimeOffset.UtcNow, ct)).Should().NotBeNull();

        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression)], ct);

        var reseeded = (await persistence.GetCronJobsAsync(predicate: null, ct)).Single();
        reseeded.Id.Should().Be(created.Id);
        reseeded.Expression.Should().Be(_SeededExpression);
        reseeded.ScheduleRevision.Should().BeGreaterThan(created.ScheduleRevision);
        reseeded.IsPaused.Should().BeTrue("the seed never writes pause state");
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(
        Microsoft.Extensions.Hosting.IHost host
    )
    {
        return host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
    }

    private static Task _SeedAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        string owner,
        (string Function, string Expression)[] declared,
        CancellationToken cancellationToken
    )
    {
        return persistence.MigrateDefinedCronJobsAsync(
            owner,
            [
                .. declared.Select(x =>
                    CronSeedDefinition.ForCronFunction(
                        owner,
                        x.Function,
                        x.Expression,
                        MissedRunPolicy.Coalesce,
                        JobsRecoveryDefaults.MissedRunGraceSeconds,
                        CronOverlapPolicy.Allow
                    )
                ),
            ],
            cancellationToken
        );
    }
}
