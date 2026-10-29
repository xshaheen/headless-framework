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

    public virtual async Task declarations_of_one_function_seed_one_row_each_with_their_declared_values()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declarations");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        byte[] euRequest = [.. "{\"region\":\"eu\"}"u8];
        byte[] usRequest = [.. "{\"region\":\"us\"}"u8];
        CronSeedDefinition[] declared =
        [
            _Declaration(
                _Owner,
                "daily-eu",
                "Europe/Berlin",
                euRequest,
                description: "EU report",
                onNodeDeath: NodeDeathPolicy.MarkFailed,
                correlationId: "corr-eu",
                causationId: "cause-eu",
                contractVersion: "2"
            ),
            _Declaration(_Owner, "daily-us", "America/New_York", usRequest),
        ];

        await persistence.MigrateDefinedCronJobsAsync(_Owner, declared, ct);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, declared, ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Should().HaveCount(2, "each declaration is its own definition, even for one function");
        var eu = rows.Should().ContainSingle(x => x.TimeZoneId == "Europe/Berlin").Subject;
        eu.Function.Should().Be(_Function);
        eu.InitIdentifier.Should().Be("headless.jobs.seed:billing_app:decl:daily-eu");
        eu.Request.Should().Equal(euRequest);
        eu.Description.Should().Be("EU report");
        eu.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        eu.CorrelationId.Should().Be("corr-eu");
        eu.CausationId.Should().Be("cause-eu");
        eu.ContractVersion.Should().Be("2");
        var us = rows.Should().ContainSingle(x => x.TimeZoneId == "America/New_York").Subject;
        us.InitIdentifier.Should().Be("headless.jobs.seed:billing_app:decl:daily-us");
        us.Request.Should().Equal(usRequest);
        us.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    public virtual async Task removing_a_declaration_deletes_its_row_and_occurrences_and_nothing_else()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-removed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var runtimeId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(runtimeId, _Function, _RuntimeExpression, NodeDeathPolicy.Retry, ct);
        await persistence.MigrateDefinedCronJobsAsync(_OtherOwner, [_Declaration(_OtherOwner, "daily-eu")], ct);
        await persistence.MigrateDefinedCronJobsAsync(
            _Owner,
            [_Declaration(_Owner, "daily-eu"), _Declaration(_Owner, "daily-us")],
            ct
        );
        var removedId = _Declaration(_Owner, "daily-eu").Id;
        await fixture.SeedCronOccurrenceAsync(
            Guid.NewGuid(),
            removedId,
            (int)JobStatus.Idle,
            ownerId: null,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            DateTime.UtcNow.AddMinutes(5),
            ct
        );

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [_Declaration(_Owner, "daily-us")], ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Select(x => x.InitIdentifier)
            .Should()
            .BeEquivalentTo(
                [null, "headless.jobs.seed:billing-app:decl:daily-eu", "headless.jobs.seed:billing_app:decl:daily-us"],
                "the runtime row and the other owner's row survive, and only this owner's removed declaration goes"
            );
        rows.Should().ContainSingle(x => x.Id == runtimeId).Which.Expression.Should().Be(_RuntimeExpression);
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == removedId, ct)).Should().BeEmpty();
    }

    public virtual async Task an_attribute_cron_and_a_declaration_of_one_function_keep_both_rows()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-attribute-and-declaration");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        CronSeedDefinition[] declared =
        [
            CronSeedDefinition.ForCronFunction(
                _Owner,
                _Function,
                _SeededExpression,
                MissedRunPolicy.Coalesce,
                JobsRecoveryDefaults.MissedRunGraceSeconds,
                CronOverlapPolicy.Allow
            ),
            _Declaration(_Owner, _Function),
        ];

        await persistence.MigrateDefinedCronJobsAsync(_Owner, declared, ct);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, declared, ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Select(x => x.InitIdentifier)
            .Should()
            .BeEquivalentTo(
                [
                    "headless.jobs.seed:billing_app:cron:seed.cleanup",
                    "headless.jobs.seed:billing_app:decl:seed.cleanup",
                ],
                "a declaration keyed like a function lives in its own marker namespace"
            );
    }

    public virtual async Task renaming_a_declaration_key_replaces_its_row()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-renamed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [_Declaration(_Owner, "daily-eu")], ct);

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [_Declaration(_Owner, "daily-europe")], ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Should()
            .ContainSingle()
            .Which.InitIdentifier.Should()
            .Be("headless.jobs.seed:billing_app:decl:daily-europe");
    }

    private static CronSeedDefinition _Declaration(
        string owner,
        string key,
        string? timeZoneId = null,
        byte[]? request = null,
        string? description = null,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry,
        string? correlationId = null,
        string? causationId = null,
        string contractVersion = JobContract.InitialVersion
    )
    {
        return CronSeedDefinition.ForDeclaration(
            owner,
            key,
            _Function,
            _SeededExpression,
            MissedRunPolicy.Coalesce,
            JobsRecoveryDefaults.MissedRunGraceSeconds,
            CronOverlapPolicy.Allow,
            contractVersion: contractVersion,
            timeZoneId: timeZoneId,
            request: request,
            description: description,
            onNodeDeath: onNodeDeath,
            correlationId: correlationId,
            causationId: causationId
        );
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
