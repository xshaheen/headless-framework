// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;

namespace Tests.Initialization;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SchemaRunnerTelemetryCollection
{
    public const string Name = "Schema runner telemetry";
}

[Collection(SchemaRunnerTelemetryCollection.Name)]
public sealed class SchemaRunnerTelemetryTests : TestBase
{
    private const string _Schema = "tenant_acme";
    private const string _Duration = "headless.schema_runner.duration";
    private const string _LockWait = "headless.schema_runner.lock.wait.duration";
    private const string _Steps = "headless.schema_runner.steps";
    private const string _Mismatches = "headless.schema_runner.mismatches";
    private const string _Races = "headless.schema_runner.absorbed_races";

    private readonly SqliteSchemaDatabase _database = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _database.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_trace_and_measure_a_cold_apply_pass_with_its_lock_wait_and_each_applied_step()
    {
        _database.LockHeldFor = TimeSpan.FromSeconds(3);
        var runner = _Runner(_Widgets("Widgets"));
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        await runner.ApplyAsync(AbortToken);

        var pass = recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.apply", StringComparison.Ordinal))
            .Which;
        pass.Status.Should().Be(ActivityStatusCode.Unset);
        pass.GetTagItem("headless.schema_runner.mode").Should().Be("apply");
        pass.GetTagItem("headless.schema_runner.outcome").Should().Be("success");
        pass.GetTagItem("headless.schema_runner.steps.applied").Should().Be(2);
        pass.GetTagItem("headless.schema_runner.mismatches").Should().Be(0);

        var lockWait = recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.lock_wait", StringComparison.Ordinal))
            .Which;
        lockWait.Parent.Should().BeSameAs(pass);
        lockWait.GetTagItem("headless.schema_runner.lock.outcome").Should().Be("acquired");

        var steps = recorder
            .Activities.Where(a => string.Equals(a.OperationName, "schema_runner.step", StringComparison.Ordinal))
            .ToList();
        steps.Should().HaveCount(2).And.OnlyContain(s => s.Parent == pass && s.Status == ActivityStatusCode.Unset);
        steps.Select(s => s.GetTagItem("headless.schema_runner.step.version")).Should().Equal("1", "2");
        steps
            .Should()
            .OnlyContain(s =>
                string.Equals(
                    (string?)s.GetTagItem("headless.schema_runner.feature"),
                    "Widgets",
                    StringComparison.Ordinal
                )
            );

        recorder
            .Of(_Duration)
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Equal(_Tags(("headless.schema_runner.mode", "apply"), ("headless.schema_runner.outcome", "success")));

        var wait = recorder.Of(_LockWait).Should().ContainSingle().Which;
        wait.Unit.Should().Be("s");
        wait.Value.Should().Be(3);
        wait.Tags.Should()
            .Equal(
                _Tags(("headless.schema_runner.dialect", "Sqlite"), ("headless.schema_runner.lock.outcome", "acquired"))
            );

        var applied = recorder.Of(_Steps);
        applied.Sum(m => m.Value).Should().Be(2);
        applied
            .Should()
            .AllSatisfy(m =>
                m.Tags.Should()
                    .Equal(
                        _Tags(
                            ("headless.schema_runner.dialect", "Sqlite"),
                            ("headless.schema_runner.feature", "Widgets"),
                            ("headless.schema_runner.step.outcome", "applied")
                        )
                    )
            );

        recorder.Of(_Mismatches).Should().BeEmpty();
        recorder.Of(_Races).Should().BeEmpty();
    }

    [Fact]
    public async Task should_count_every_step_as_skipped_and_take_no_lock_when_the_database_is_warm()
    {
        var runner = _Runner(_Widgets("Widgets"));
        await runner.ApplyAsync(AbortToken);
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        await runner.ApplyAsync(AbortToken);

        var skipped = recorder.Of(_Steps).Should().ContainSingle().Which;
        skipped.Value.Should().Be(2);
        skipped.Tags["headless.schema_runner.step.outcome"].Should().Be("skipped");
        recorder.Of(_LockWait).Should().BeEmpty("a warm database costs no lock");
        recorder.Activities.Should().ContainSingle().Which.OperationName.Should().Be("schema_runner.apply");
    }

    [Fact]
    public async Task should_count_recorded_steps_as_skipped_and_new_ones_as_applied_when_a_step_is_added()
    {
        await _Runner(_database.Contribution("Widgets", _Schema, _TableStep)).ApplyAsync(AbortToken);
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName, spans: false);

        await _Runner(_Widgets("Widgets")).ApplyAsync(AbortToken);

        recorder
            .Of(_Steps)
            .Select(m => ((string?)m.Tags["headless.schema_runner.step.outcome"], m.Value))
            .Should()
            .BeEquivalentTo([("skipped", 1d), ("applied", 1d)]);
    }

    [Fact]
    public async Task should_count_each_missing_step_by_kind_when_verify_finds_an_empty_database()
    {
        var runner = _Runner(_Widgets("Widgets"));
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        var mismatches = await runner.VerifyAsync(AbortToken);

        mismatches.Should().HaveCount(2);
        var missing = recorder.Of(_Mismatches);
        missing.Should().HaveCount(2).And.OnlyContain(m => m.Value == 1);
        missing[0]
            .Tags.Should()
            .Equal(
                _Tags(
                    ("headless.schema_runner.mode", "verify"),
                    ("headless.schema_runner.dialect", "Sqlite"),
                    ("headless.schema_runner.feature", "Widgets"),
                    ("headless.schema_runner.mismatch.kind", "missing")
                )
            );

        var pass = recorder.Activities.Should().ContainSingle().Which;
        pass.OperationName.Should().Be("schema_runner.verify");
        pass.GetTagItem("headless.schema_runner.outcome").Should().Be("success");
        pass.GetTagItem("headless.schema_runner.mismatches").Should().Be(2);
    }

    [Fact]
    public async Task should_record_a_failed_verify_pass_when_startup_refuses_a_missing_step()
    {
        var runner = _Runner(_Widgets("Widgets"));
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        var act = () => runner.RunAsync(SchemaRunnerMode.Verify, AbortToken);

        await act.Should().ThrowAsync<SchemaRunnerException>();
        recorder
            .Of(_Duration)
            .Should()
            .ContainSingle()
            .Which.Tags.Should()
            .Equal(
                _Tags(
                    ("headless.schema_runner.mode", "verify"),
                    ("headless.schema_runner.outcome", "failure"),
                    ("error.type", typeof(SchemaRunnerException).FullName)
                )
            );
        recorder.Of(_Mismatches).Should().HaveCount(2);

        var pass = recorder.Activities.Should().ContainSingle().Which;
        pass.Status.Should().Be(ActivityStatusCode.Error);
        pass.StatusDescription.Should().BeNull();
        pass.GetTagItem("error.type").Should().Be(typeof(SchemaRunnerException).FullName);
    }

    [Fact]
    public async Task should_count_changed_checksums_and_unknown_rows_found_by_an_apply_pass()
    {
        await _Runner(_Widgets("Widgets")).ApplyAsync(AbortToken);
        _database.Execute($"UPDATE {_Schema}_history SET checksum = 'edited' WHERE version = '1'");
        _database.Execute($"INSERT INTO {_Schema}_history VALUES ('Widgets', '9', 'newer', 'from a newer replica')");
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName, spans: false);

        var act = () => _Runner(_Widgets("Widgets")).RunAsync(SchemaRunnerMode.Apply, AbortToken);

        await act.Should().ThrowAsync<SchemaRunnerException>();
        recorder
            .Of(_Mismatches)
            .Select(m => m.Tags["headless.schema_runner.mismatch.kind"])
            .Should()
            .BeEquivalentTo(["checksum", "unknown"]);
        recorder
            .Of(_Mismatches)
            .Should()
            .OnlyContain(m =>
                string.Equals((string?)m.Tags["headless.schema_runner.mode"], "apply", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_record_a_timed_out_lock_wait_and_a_failed_pass_when_the_lock_is_never_granted()
    {
        _database.LockUnavailable = true;
        var runner = new SchemaRunner([_Widgets("Widgets")], timeProvider: _database.Clock, lockTimeout: TimeSpan.Zero);
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        var act = () => runner.ApplyAsync(AbortToken);

        await act.Should().ThrowAsync<SchemaRunnerException>().WithMessage("*timed out*");
        recorder
            .Of(_LockWait)
            .Should()
            .ContainSingle()
            .Which.Tags["headless.schema_runner.lock.outcome"]
            .Should()
            .Be("timed_out");
        recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.lock_wait", StringComparison.Ordinal))
            .Which.Status.Should()
            .Be(ActivityStatusCode.Error);
        recorder
            .Of(_Duration)
            .Should()
            .ContainSingle()
            .Which.Tags["headless.schema_runner.outcome"]
            .Should()
            .Be("failure");
    }

    [Fact]
    public async Task should_mark_the_failed_step_span_as_an_error_when_a_step_fails()
    {
        var runner = _Runner(
            _database.Contribution("Widgets", _Schema, new SchemaStep("1", "broken", "SELECT * FROM no_such_table"))
        );
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        var act = () => runner.ApplyAsync(AbortToken);

        await act.Should().ThrowAsync<SchemaRunnerException>();
        recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.step", StringComparison.Ordinal))
            .Which.Status.Should()
            .Be(ActivityStatusCode.Error);
        recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.apply", StringComparison.Ordinal))
            .Which.GetTagItem("error.type")
            .Should()
            .Be(typeof(SchemaRunnerException).FullName);
        recorder.Of(_Steps).Should().BeEmpty();
    }

    [Fact]
    public async Task should_count_an_absorbed_race_when_another_creator_committed_the_object_first()
    {
        _database.RaceNext(1);
        var runner = _Runner(
            _database.Contribution("Widgets", _Schema, new SchemaStep("1", "racy", "SELECT race_once()"))
        );
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        var result = await runner.ApplyAsync(AbortToken);

        result.AbsorbedRaces.Should().Be(1);
        var races = recorder.Of(_Races).Should().ContainSingle().Which;
        races.Value.Should().Be(1);
        races.Tags.Should().Equal(_Tags(("headless.schema_runner.dialect", "Sqlite")));
        recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.step", StringComparison.Ordinal))
            .Which.GetTagItem("headless.schema_runner.absorbed_races")
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_tag_only_the_feature_name_and_never_the_schema_configured_names_or_connection()
    {
        var runner = _Runner(_Widgets("Widgets:acme_widgets"));
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName);

        await runner.ApplyAsync(AbortToken);
        await runner.VerifyAsync(AbortToken);

        var values = recorder
            .Measurements.SelectMany(m => m.Tags.Values)
            .Concat(recorder.Activities.SelectMany(a => a.TagObjects.Select(t => t.Value)))
            .OfType<string>()
            .ToList();

        values.Should().Contain("Widgets");
        values.Should().NotContain(v => v.Contains(_Schema, StringComparison.Ordinal));
        values.Should().NotContain(v => v.Contains("acme_widgets", StringComparison.Ordinal));
        values.Should().NotContain(v => v.Contains(_database.ConnectionString, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_run_a_step_under_its_step_span_and_restore_the_callers_span_afterwards()
    {
        using var outer = RecordedTestActivity.Start();
        var runner = _Runner(
            _database.Contribution("Widgets", _Schema, new SchemaStep("1", "probe", "SELECT capture_activity()"))
        );
        using var recorder = new TelemetryRecorder(SchemaRunnerDiagnostics.SourceName, metrics: false);

        await runner.ApplyAsync(AbortToken);

        var step = recorder
            .Activities.Should()
            .ContainSingle(a => string.Equals(a.OperationName, "schema_runner.step", StringComparison.Ordinal))
            .Which;
        _database.CapturedActivity.Should().BeSameAs(step);
        step.Parent!.Parent.Should().BeSameAs(outer.Activity);
        Activity.Current.Should().BeSameAs(outer.Activity);
    }

    [Fact]
    public async Task should_start_no_span_when_nothing_listens()
    {
        using var outer = RecordedTestActivity.Start();
        var runner = _Runner(
            _database.Contribution("Widgets", _Schema, new SchemaStep("1", "probe", "SELECT capture_activity()"))
        );

        await runner.ApplyAsync(AbortToken);

        _database
            .CapturedActivity.Should()
            .BeSameAs(outer.Activity, "with no listener every emission point is skipped");
    }

    private SchemaRunner _Runner(params SchemaContribution[] contributions)
    {
        return new SchemaRunner(contributions, timeProvider: _database.Clock);
    }

    private static readonly SchemaStep _TableStep = new(
        "1",
        "widgets table",
        $"CREATE TABLE IF NOT EXISTS {_Schema}_widgets (id INTEGER)"
    );

    private static readonly SchemaStep _IndexStep = new(
        "2",
        "widgets index",
        $"CREATE INDEX IF NOT EXISTS ix_{_Schema}_widgets ON {_Schema}_widgets (id)"
    );

    private SchemaContribution _Widgets(string feature)
    {
        return _database.Contribution(feature, _Schema, _TableStep, _IndexStep);
    }

    private static Dictionary<string, object?> _Tags(params (string Key, object? Value)[] tags)
    {
        return tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
    }
}
