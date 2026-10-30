// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
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
    private static readonly byte[] _UsRequest = [.. "{\"region\":\"us\"}"u8];
    private static readonly string _UsHash = _CanonicalHash("{\"region\":\"us\"}");

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

    public virtual async Task seed_keeps_the_rows_of_an_owner_that_differs_only_by_case()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-owner-case");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var upperOwner = _Owner.ToUpperInvariant();

        await _SeedAsync(persistence, upperOwner, [(_Function, _SeededExpression)], ct);
        await _SeedAsync(persistence, _Owner, [("seed.kept", _SeededExpression)], ct);

        var rows = await persistence.GetCronJobsAsync(predicate: null, ct);
        rows.Select(x => x.InitIdentifier)
            .Should()
            .BeEquivalentTo(
                [$"headless.jobs.seed:{upperOwner}:cron:{_Function}", $"headless.jobs.seed:{_Owner}:cron:seed.kept"],
                "a case-insensitive collation must not make one owner's seed delete another owner's rows"
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

    public virtual async Task removing_a_declaration_with_a_running_occurrence_retires_it_and_deletes_pending_occurrences()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-retire-running");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us");
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        var statuses = new[] { JobStatus.Idle, JobStatus.Queued, JobStatus.InProgress, JobStatus.Succeeded };
        var ids = statuses.Select(_ => Guid.NewGuid()).ToArray();
        var executionTime = DateTime.UtcNow;
        for (var index = 0; index < statuses.Length; index++)
        {
            await fixture.SeedCronOccurrenceAsync(
                ids[index],
                declared.Id,
                (int)statuses[index],
                "seed-worker",
                NodeDeathPolicy.Retry,
                executionTime.AddMinutes(10),
                executionTime.AddMinutes(index),
                ct
            );
        }

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [], ct);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [], ct);

        var retired = await persistence.GetCronJobByIdAsync(declared.Id, ct);
        retired.Should().NotBeNull();
        retired!.IsPaused.Should().BeTrue();
        retired.InitIdentifier.Should().Be("headless.jobs.seed:billing_app:retired:decl:daily-us");
        retired.UpdatedAt.Should().BeOnOrAfter(beforeSeed.UpdatedAt);
        var kept = await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == declared.Id, ct);
        kept.Select(x => x.Id).Should().BeEquivalentTo(ids.Skip(2));
        kept.Select(x => x.Status).Should().BeEquivalentTo([JobStatus.InProgress, JobStatus.Succeeded]);
    }

    public virtual async Task the_next_seed_deletes_a_retired_declaration_after_its_running_occurrence_completes()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-retired-completed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us");
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var occurrenceId = Guid.NewGuid();
        await fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            declared.Id,
            (int)JobStatus.InProgress,
            "seed-worker",
            NodeDeathPolicy.Retry,
            DateTime.UtcNow.AddMinutes(10),
            DateTime.UtcNow,
            ct
        );
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [], ct);
        var completed = new JobExecutionState
        {
            JobId = occurrenceId,
            FunctionName = declared.Function,
            ClaimOwner = "seed-worker",
        };
        completed.SetProperty(x => x.Status, JobStatus.Succeeded);
        (await persistence.UpdateCronJobOccurrenceAsync(completed, ct))
            .Should()
            .Be(1, "retirement must keep the occurrence writable by its running worker");

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [], ct);

        (await persistence.GetCronJobByIdAsync(declared.Id, ct)).Should().BeNull();
        (await persistence.GetAllCronJobOccurrencesAsync(x => x.CronJobId == declared.Id, ct)).Should().BeEmpty();
    }

    public virtual async Task redeclaring_a_retired_row_restores_and_unpauses_it_even_when_its_schedule_has_not_changed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-retired-redeclared");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us");
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var executionTime = DateTime.UtcNow;
        await fixture.SeedCronOccurrenceAsync(
            Guid.NewGuid(),
            declared.Id,
            (int)JobStatus.InProgress,
            "seed-worker",
            NodeDeathPolicy.Retry,
            executionTime.AddMinutes(10),
            executionTime,
            ct
        );
        // Removing the declaration while its run is in progress retires the row.
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [], ct);
        var retired = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        retired.InitIdentifier.Should().Be("headless.jobs.seed:billing_app:retired:decl:daily-us");
        retired.IsPaused.Should().BeTrue();
        var revisionBefore = retired.ScheduleRevision;

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var restored = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        restored.InitIdentifier.Should().Be(declared.Marker);
        restored.IsPaused.Should().BeFalse();
        restored.Expression.Should().Be(declared.Expression);
        restored.ScheduleRevision.Should().Be(revisionBefore + 1);
        restored.ReconciledThroughUtc.Should().Be(default);
        restored.NextDueUtc.Should().Be(default);
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

    public virtual async Task seed_restores_a_declarations_zone_an_operator_changed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-zone");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(
            _Owner,
            "daily-us",
            timeZoneId: "America/New_York",
            request: _UsRequest,
            requestHash: _UsHash
        );
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var edited = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        edited.TimeZoneId = "Europe/Berlin";
        (await persistence.UpdateCronJobsAsync([edited], ct)).Should().Be(1);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var reseeded = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        reseeded.TimeZoneId.Should().Be("America/New_York", "code wins on the zone");
        reseeded.Expression.Should().Be(_SeededExpression);
        reseeded.ScheduleRevision.Should().Be(beforeSeed.ScheduleRevision + 1);
    }

    public virtual async Task seed_restores_the_declared_job_and_arguments_of_a_repointed_row()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-repointed");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var edited = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        edited.Function = "seed.other";
        (await persistence.UpdateCronJobsAsync([edited], ct)).Should().Be(1);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        beforeSeed.Function.Should().Be("seed.other");

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var reseeded = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        reseeded.Function.Should().Be(_Function, "code wins on the job a declaration names");
        reseeded.Request.Should().Equal(_UsRequest, "the declared arguments come back with the declared job");
        reseeded.ScheduleRevision.Should().Be(beforeSeed.ScheduleRevision + 1);
    }

    public virtual async Task concurrent_seeds_restore_a_drifted_declaration_once_and_delete_a_removed_one()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("seed-converge-drift-a");
        using var secondHost = fixture.BuildHost("seed-converge-drift-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        var first = _Persistence(firstHost);
        var second = _Persistence(secondHost);
        var kept = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        var removed = _Declaration(_Owner, "daily-eu", request: _UsRequest, requestHash: _UsHash);
        await first.MigrateDefinedCronJobsAsync(_Owner, [kept, removed], ct);
        var edited = (await first.GetCronJobByIdAsync(kept.Id, ct))!;
        edited.Expression = _RuntimeExpression;
        (await first.UpdateCronJobsAsync([edited], ct)).Should().Be(1);
        var beforeSeed = (await first.GetCronJobByIdAsync(kept.Id, ct))!;

        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(
                first.MigrateDefinedCronJobsAsync(_Owner, [kept], ct),
                second.MigrateDefinedCronJobsAsync(_Owner, [kept], ct)
            );
        }

        var rows = await first.GetCronJobsAsync(predicate: null, ct);
        var row = rows.Should().ContainSingle("the removed declaration is gone and the kept one stays").Subject;
        row.Id.Should().Be(kept.Id);
        row.Expression.Should().Be(_SeededExpression);
        row.ScheduleRevision.Should()
            .Be(beforeSeed.ScheduleRevision + 1, "the seed that loses the row lock sees the restored row and skips");
    }

    public virtual async Task a_runtime_update_without_the_seed_marker_keeps_the_row_seeded()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-runtime-update-marker");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var edited = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        // A dashboard form posts the definition without the seed marker.
        edited.InitIdentifier = null;
        edited.Retries = 4;
        (await persistence.UpdateCronJobsAsync([edited], ct)).Should().Be(1);

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var reseeded = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        reseeded.InitIdentifier.Should().Be(declared.Marker, "the seed marker belongs to the seed");
        reseeded.Retries.Should().Be(4, "the operator's edit still applies");
    }

    public virtual async Task seed_restores_a_declarations_expression_and_keeps_the_operators_retries_and_pause()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-operator-edit");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        (await persistence.PauseCronJobAsync(declared.Id, DateTimeOffset.UtcNow, ct)).Should().NotBeNull();
        var edited = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        edited.Expression = _RuntimeExpression;
        edited.Retries = 7;
        edited.RetryIntervals = [5, 5, 5, 5, 5, 5, 5];
        (await persistence.UpdateCronJobsAsync([edited], ct)).Should().Be(1);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var reseeded = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        reseeded.Expression.Should().Be(_SeededExpression, "code wins on the expression");
        reseeded.ScheduleRevision.Should().Be(beforeSeed.ScheduleRevision + 1);
        reseeded.Retries.Should().Be(7, "retries are written only when the definition is created");
        reseeded.RetryIntervals.Should().Equal(5, 5, 5, 5, 5, 5, 5);
        reseeded.IsPaused.Should().BeTrue("the seed never writes pause state");
        reseeded.Request.Should().Equal(_UsRequest);
    }

    public virtual async Task changed_declared_arguments_rewrite_the_payload_and_version_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-arguments");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var original = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [original], ct);
        var created = (await persistence.GetCronJobByIdAsync(original.Id, ct))!;
        byte[] eastRequest = [.. "{\"region\":\"us-east\"}"u8];
        var changed = _Declaration(
            _Owner,
            "daily-us",
            request: eastRequest,
            contractVersion: "2",
            requestHash: _CanonicalHash("{\"region\":\"us-east\"}")
        );

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [changed], ct);

        var rewritten = (await persistence.GetCronJobByIdAsync(original.Id, ct))!;
        rewritten.Request.Should().Equal(eastRequest, "code wins on the arguments");
        rewritten.ContractVersion.Should().Be("2", "the payload and its version are rewritten together");
        rewritten.ScheduleRevision.Should().Be(created.ScheduleRevision + 1);
        rewritten.Expression.Should().Be(_SeededExpression);

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [changed], ct);

        var unchanged = (await persistence.GetCronJobByIdAsync(original.Id, ct))!;
        unchanged
            .ScheduleRevision.Should()
            .Be(rewritten.ScheduleRevision, "the restored arguments match the declaration");
        unchanged.UpdatedAt.Should().Be(rewritten.UpdatedAt);
    }

    public virtual async Task the_same_arguments_written_compressed_and_reordered_are_not_rewritten()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-canonical");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        // Another host's writer: indented, members in another order, then compressed.
        var compressed = JobsHelper.CreateJobRequest<byte[]>(
            [.. "{\n  \"zone\": \"America/New_York\",\n  \"region\": \"us\"\n}"u8],
            new JobsRequestSerializationOptions { UseGZipCompression = true }
        );
        var hash = _CanonicalHash("{\"region\":\"us\",\"zone\":\"America/New_York\"}");
        var first = _Declaration(_Owner, "daily-us", request: compressed, requestHash: hash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [first], ct);
        var stored = (await persistence.GetCronJobByIdAsync(first.Id, ct))!;
        byte[] plain = [.. "{\"region\":\"us\",\"zone\":\"America/New_York\"}"u8];

        await persistence.MigrateDefinedCronJobsAsync(
            _Owner,
            [_Declaration(_Owner, "daily-us", request: plain, requestHash: hash)],
            ct
        );

        var reseeded = (await persistence.GetCronJobByIdAsync(first.Id, ct))!;
        reseeded.Request.Should().Equal(compressed, "the same arguments compare equal however they were written");
        reseeded.ScheduleRevision.Should().Be(stored.ScheduleRevision);
    }

    public virtual async Task a_changed_contract_version_with_unchanged_arguments_is_rewritten_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-version");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var original = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [original], ct);
        var created = (await persistence.GetCronJobByIdAsync(original.Id, ct))!;
        var bumped = _Declaration(_Owner, "daily-us", request: _UsRequest, contractVersion: "2", requestHash: _UsHash);

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [bumped], ct);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [bumped], ct);

        var relabelled = (await persistence.GetCronJobByIdAsync(original.Id, ct))!;
        relabelled.ContractVersion.Should().Be("2");
        relabelled.Request.Should().Equal(_UsRequest);
        relabelled.ScheduleRevision.Should().Be(created.ScheduleRevision + 1, "only the first seed finds drift");
    }

    public virtual async Task a_stored_payload_that_is_not_json_is_overwritten_without_failing_the_seed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-declaration-corrupt");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        var declared = _Declaration(_Owner, "daily-us", request: _UsRequest, requestHash: _UsHash);
        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);
        var corrupted = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        corrupted.Request = [0x7b, 0xff, 0x00];
        (await persistence.UpdateCronJobsAsync([corrupted], ct)).Should().Be(1);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;

        await persistence.MigrateDefinedCronJobsAsync(_Owner, [declared], ct);

        var restored = (await persistence.GetCronJobByIdAsync(declared.Id, ct))!;
        restored.Request.Should().Equal(_UsRequest);
        restored.ScheduleRevision.Should().Be(beforeSeed.ScheduleRevision + 1);
    }

    public virtual async Task an_attribute_cron_rows_payload_and_version_are_never_rewritten()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("seed-attribute-payload");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = _Persistence(host);
        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression)], ct);
        var row = (await persistence.GetCronJobsAsync(predicate: null, ct)).Single();
        var edited = (await persistence.GetCronJobByIdAsync(row.Id, ct))!;
        edited.Request = [.. "{\"edited\":true}"u8];
        edited.ContractVersion = "9";
        (await persistence.UpdateCronJobsAsync([edited], ct)).Should().Be(1);
        var beforeSeed = (await persistence.GetCronJobByIdAsync(row.Id, ct))!;

        await _SeedAsync(persistence, _Owner, [(_Function, _SeededExpression)], ct);

        var reseeded = (await persistence.GetCronJobByIdAsync(row.Id, ct))!;
        reseeded.Request.Should().Equal(edited.Request, "an attribute cron declares no arguments to restore");
        reseeded.ContractVersion.Should().Be("9");
        reseeded.ScheduleRevision.Should().Be(beforeSeed.ScheduleRevision);
    }

    // The canonical-hash contract of a seed entry, computed here from hand-written canonical JSON rather than by the
    // code under test.
    private static string _CanonicalHash(string canonicalJson)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
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
        string contractVersion = JobContract.InitialVersion,
        string? requestHash = null
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
            requestHash ?? _CanonicalHash(request is null ? "" : Encoding.UTF8.GetString(request)),
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
