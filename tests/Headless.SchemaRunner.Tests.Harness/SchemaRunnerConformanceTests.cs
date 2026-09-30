// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The schema runner's contract against a real database, shared by every provider: concurrent replicas apply each step
/// exactly once, a foreign creator's object is absorbed, verify mode catches a missing step and a changed checksum,
/// and the exported script run by the plain client yields a schema verify mode accepts.
/// </summary>
public abstract class SchemaRunnerConformanceTests(ISchemaRunnerFixture fixture) : TestBase
{
    private const int _Replicas = 5;
    private const int _ConcurrencyRounds = 10;

    [Fact]
    public async Task should_apply_every_step_exactly_once_when_replicas_start_together()
    {
        var schema = _Schema("concurrent");

        // Several rounds from an empty database, because one passing race proves little.
        for (var round = 0; round < _ConcurrencyRounds; round++)
        {
            await fixture.DropSchemaAsync(schema, AbortToken);
            var runners = Enumerable.Range(0, _Replicas).Select(_ => fixture.CreateRunner(schema)).ToArray();
            using var start = new ManualResetEventSlim();

            var runs = runners
                .Select(runner =>
                    Task.Run(
                        async () =>
                        {
                            start.Wait(AbortToken);

                            return await runner.ApplyAsync(AbortToken);
                        },
                        AbortToken
                    )
                )
                .ToArray();

            start.Set();
            var results = await Task.WhenAll(runs);

            var applied = results.SelectMany(r => r.AppliedSteps).ToList();
            applied.Should().HaveCount(fixture.ExpectedStepCount, $"round {round} must apply each step once in total");
            applied.Should().OnlyHaveUniqueItems($"round {round} must not apply any step twice");
            results.Should().OnlyContain(r => r.AbsorbedRaces == 0, "replicas serialize on the lock and never race");
            results.Should().OnlyContain(r => r.Mismatches.Count == 0);
            (await fixture.CountHistoryRowsAsync(schema, AbortToken)).Should().Be(fixture.ExpectedStepCount);
            (await fixture.CountTablesAsync(schema, AbortToken)).Should().Be(fixture.ExpectedTableCount);
        }

        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_absorb_a_foreign_creator_that_commits_the_schema_and_a_table_first()
    {
        var schema = _Schema("foreign");
        await fixture.DropSchemaAsync(schema, AbortToken);

        await using var foreign = await fixture.BeginForeignCreatorAsync(schema, AbortToken);
        var apply = fixture.CreateRunner(schema).ApplyAsync(AbortToken);

        // Commit only once the runner waits on the foreign transaction, so the race is forced, not hoped for.
        var waited = Stopwatch.StartNew();
        var blocked = false;

        while (!apply.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (await foreign.IsBlockingAnotherSessionAsync(AbortToken))
            {
                blocked = true;

                break;
            }

            await Task.Delay(25, AbortToken);
        }

        await foreign.CommitAsync(AbortToken);
        var result = await apply;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{fixture.Dialect.Name}: runner blocked on the foreign creator = {blocked}, absorbed races = {result.AbsorbedRaces}"
        );

        result.AppliedSteps.Should().HaveCount(fixture.ExpectedStepCount);
        result.Mismatches.Should().BeEmpty();
        (await fixture.CountHistoryRowsAsync(schema, AbortToken)).Should().Be(fixture.ExpectedStepCount);
        (await fixture.CountTablesAsync(schema, AbortToken)).Should().Be(fixture.ExpectedTableCount);

        if (fixture.ForeignCreatorForcesRerun)
        {
            blocked.Should().BeTrue("the runner's CREATE SCHEMA must wait on the uncommitted foreign one");
            result.AbsorbedRaces.Should().Be(1, "the first attempt fails on the catalog and the re-run succeeds");
        }

        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_report_every_step_missing_when_verifying_an_empty_database()
    {
        var schema = _Schema("verify_empty");
        await fixture.DropSchemaAsync(schema, AbortToken);

        var mismatches = await fixture.CreateRunner(schema).VerifyAsync(AbortToken);

        mismatches.Should().HaveCount(fixture.ExpectedStepCount);
        mismatches.Should().OnlyContain(m => m.Kind == SchemaMismatchKind.Missing);
    }

    [Fact]
    public async Task should_report_exactly_the_missing_step_when_the_database_lacks_one()
    {
        var schema = _Schema("verify_one");
        await fixture.DropSchemaAsync(schema, AbortToken);
        await fixture.CreateRunner(schema).ApplyAsync(AbortToken);
        await fixture.DeleteHistoryRowAsync(schema, "Coordination", "2", AbortToken);

        var mismatches = await fixture.CreateRunner(schema).VerifyAsync(AbortToken);

        mismatches
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<SchemaMismatch>(m =>
                m.Kind == SchemaMismatchKind.Missing && m.Feature == "Coordination" && m.Version == "2"
            );
        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_pass_verify_after_apply()
    {
        var schema = _Schema("verify_ok");
        await fixture.DropSchemaAsync(schema, AbortToken);
        await fixture.CreateRunner(schema).ApplyAsync(AbortToken);

        var mismatches = await fixture.CreateRunner(schema).VerifyAsync(AbortToken);

        mismatches.Should().BeEmpty();
        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_report_a_changed_checksum_in_both_modes_and_never_rerun_the_step()
    {
        var schema = _Schema("checksum");
        await fixture.DropSchemaAsync(schema, AbortToken);
        await fixture.CreateRunner(schema).ApplyAsync(AbortToken);
        await fixture.TamperChecksumAsync(schema, "Idempotency", "1", AbortToken);

        var verified = await fixture.CreateRunner(schema).VerifyAsync(AbortToken);
        var applied = await fixture.CreateRunner(schema).ApplyAsync(AbortToken);

        verified
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<SchemaMismatch>(m =>
                m.Kind == SchemaMismatchKind.Checksum && m.Feature == "Idempotency" && m.Version == "1"
            );
        applied.Mismatches.Should().ContainSingle(m => m.Kind == SchemaMismatchKind.Checksum);
        applied.AppliedSteps.Should().BeEmpty("a recorded step is never re-run, even when its SQL changed");
        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_yield_a_verified_schema_when_the_plain_client_runs_the_exported_script_twice()
    {
        var schema = _Schema("export");
        await fixture.DropSchemaAsync(schema, AbortToken);
        var script = fixture.CreateRunner(schema).ExportScript(fixture.Dialect);
        TestContext.Current.TestOutputHelper?.WriteLine(script);

        await fixture.ExecuteScriptWithPlainClientAsync(script, AbortToken);
        await fixture.ExecuteScriptWithPlainClientAsync(script, AbortToken);

        (await fixture.CreateRunner(schema).VerifyAsync(AbortToken)).Should().BeEmpty();
        (await fixture.CountHistoryRowsAsync(schema, AbortToken)).Should().Be(fixture.ExpectedStepCount);
        (await fixture.CountTablesAsync(schema, AbortToken)).Should().Be(fixture.ExpectedTableCount);
        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_start_in_apply_mode_and_create_every_feature_table()
    {
        var schema = _Schema("host_apply");
        await fixture.DropSchemaAsync(schema, AbortToken);
        using var host = fixture.CreateHost(schema, SchemaRunnerMode.Apply);

        await host.StartAsync(AbortToken);

        host.Services.GetServices<IInitializer>().Should().ContainSingle().Which.IsInitialized.Should().BeTrue();
        (await fixture.CountTablesAsync(schema, AbortToken)).Should().Be(fixture.ExpectedTableCount);
        await host.StopAsync(AbortToken);
        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    [Fact]
    public async Task should_refuse_to_start_in_verify_mode_when_a_step_is_missing()
    {
        var schema = _Schema("host_verify");
        await fixture.DropSchemaAsync(schema, AbortToken);
        using var host = fixture.CreateHost(schema, SchemaRunnerMode.Verify);

        var act = () => host.StartAsync(AbortToken);

        (await act.Should().ThrowAsync<SchemaRunnerException>())
            .Which.Mismatches.Should()
            .HaveCount(fixture.ExpectedStepCount)
            .And.OnlyContain(m => m.Kind == SchemaMismatchKind.Missing);
        host.Services.GetServices<IInitializer>().Single().IsInitialized.Should().BeFalse();
        (await fixture.CountTablesAsync(schema, AbortToken)).Should().Be(0, "verify mode writes nothing");
    }

    /// <summary>
    /// Records startup cost on a warm database: the runner's one pass against the deleted per-feature protocol. It
    /// asserts nothing about speed; the numbers go to the test output and the measurement file for the PR.
    /// </summary>
    [Fact]
    [Trait("Category", "Measurement")]
    public async Task should_record_warm_startup_cost_of_the_runner_and_the_legacy_protocol()
    {
        const int samples = 40;
        var schema = _Schema("measure");
        await fixture.DropSchemaAsync(schema, AbortToken);

        var coldRunner = Stopwatch.StartNew();
        await fixture.CreateRunner(schema).ApplyAsync(AbortToken);
        coldRunner.Stop();

        // Warm both paths once so connection pools and plans are hot before sampling.
        var runner = fixture.CreateRunner(schema);
        await runner.ApplyAsync(AbortToken);
        await fixture.RunLegacyInitializerProtocolAsync(schema, AbortToken);

        var runnerSamples = new List<double>(samples);
        var legacySamples = new List<double>(samples);

        for (var i = 0; i < samples; i++)
        {
            var sample = Stopwatch.StartNew();
            var result = await runner.ApplyAsync(AbortToken);
            runnerSamples.Add(sample.Elapsed.TotalMilliseconds);
            result.AppliedSteps.Should().BeEmpty();

            sample.Restart();
            await fixture.RunLegacyInitializerProtocolAsync(schema, AbortToken);
            legacySamples.Add(sample.Elapsed.TotalMilliseconds);
        }

        var report = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            dialect={fixture.Dialect.Name} features=3 steps={fixture.ExpectedStepCount} samples={samples}
            cold runner apply: {coldRunner.Elapsed.TotalMilliseconds:F1} ms
            warm runner apply: median {_Median(runnerSamples):F2} ms, p90 {_Percentile(runnerSamples, 0.9):F2} ms
            warm legacy protocol (3 initializers, sequential): median {_Median(legacySamples):F2} ms, p90 {_Percentile(
                legacySamples,
                0.9
            ):F2} ms
            """
        );

        TestContext.Current.TestOutputHelper?.WriteLine(report);

        if (Environment.GetEnvironmentVariable("SCHEMA_RUNNER_MEASUREMENTS_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{fixture.Dialect.Name}.txt"), report, AbortToken);
        }

        await fixture.DropSchemaAsync(schema, AbortToken);
    }

    private string _Schema(string scenario)
    {
        return $"runner_{scenario}_{fixture.Dialect.Name.ToLowerInvariant()}";
    }

    private static double _Median(List<double> values)
    {
        return _Percentile(values, 0.5);
    }

    private static double _Percentile(List<double> values, double percentile)
    {
        var sorted = values.Order().ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;

        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
