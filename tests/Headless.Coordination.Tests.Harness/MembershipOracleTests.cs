// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>The first step at which a store disagreed with the model, with the smallest history found that still shows it.</summary>
public sealed record MembershipOracleDivergence(
    MembershipOracleHistory History,
    int Step,
    string Model,
    string Actual,
    MembershipOracleHistory Minimal,
    bool ReproducedOnReplay
)
{
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"seed {History.Seed}: diverged at step {Step} ({History.Ops[Step]}); replay with {MembershipOracleSeeds.SeedVariable}={History.Seed}"
        );
        builder.AppendLine(CultureInfo.InvariantCulture, $"  model: {Model}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  store: {Actual}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  replayed identically: {ReproducedOnReplay}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  minimal history ({Minimal.Ops.Count} ops):");
        builder.Append(Minimal.Describe());

        return builder.ToString();
    }
}

/// <summary>Runs generated histories against the model and a store, step by step, and reports where they differ.</summary>
public static class MembershipDifferentialOracle
{
    // Bounds the reruns minimization may spend on one divergence.
    private const int _MaxShrinkRuns = 60;

    public static async Task<(int Step, string Model, string Actual)?> FindFirstDivergenceAsync(
        ICoordinationOracleFixture fixture,
        MembershipOracleHistory history,
        CancellationToken cancellationToken
    )
    {
        await using var model = new MembershipOracleModel(new MembershipOracleScope(history));
        await using var store = await MembershipOracleSession
            .StartAsync(fixture, new MembershipOracleScope(history), pruneOnEverySnapshot: true, cancellationToken)
            .ConfigureAwait(false);
        var clock = Stopwatch.StartNew();

        for (var step = 0; step < history.Ops.Count; step++)
        {
            var expected = await model.ExecuteAsync(history.Ops[step], cancellationToken).ConfigureAwait(false);
            var observed = await store.ExecuteAsync(history.Ops[step], cancellationToken).ConfigureAwait(false);

            // Past this, the real time a row has aged could carry it across a classification boundary the model's
            // virtual time stays clear of, so a difference would say nothing about the store.
            if (clock.Elapsed > MembershipOracleGenerator.MaxRealTime)
            {
                throw new InvalidOperationException(
                    $"Seed {history.Seed} ran {clock.Elapsed} of real time by step {step}, past the "
                        + $"{MembershipOracleGenerator.MaxRealTime} its boundary margins allow."
                );
            }

            if (!string.Equals(expected, observed, StringComparison.Ordinal))
            {
                return (step, expected, observed);
            }
        }

        return null;
    }

    /// <summary>Checks one history; on divergence, shrinks it and replays the shrunk history to prove it is deterministic.</summary>
    public static async Task<MembershipOracleDivergence?> CheckAsync(
        ICoordinationOracleFixture fixture,
        MembershipOracleHistory history,
        CancellationToken cancellationToken
    )
    {
        if (await FindFirstDivergenceAsync(fixture, history, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            return null;
        }

        var minimal = await _ShrinkAsync(
                fixture,
                history.With([.. history.Ops.Take(found.Step + 1)]),
                cancellationToken
            )
            .ConfigureAwait(false);
        var replay = await FindFirstDivergenceAsync(fixture, minimal, cancellationToken).ConfigureAwait(false);

        return new MembershipOracleDivergence(
            history,
            found.Step,
            found.Model,
            found.Actual,
            minimal,
            replay is { } again && again.Step == minimal.Ops.Count - 1
        );
    }

    private static async Task<MembershipOracleHistory> _ShrinkAsync(
        ICoordinationOracleFixture fixture,
        MembershipOracleHistory history,
        CancellationToken cancellationToken
    )
    {
        var ops = history.Ops.ToList();
        var runs = 0;

        // Drop one earlier operation at a time, keeping the drop whenever the last operation still diverges.
        for (var i = ops.Count - 2; i >= 0 && runs < _MaxShrinkRuns; i--)
        {
            var candidate = ops.Where((_, index) => index != i).ToList();
            runs++;

            var result = await FindFirstDivergenceAsync(fixture, history.With(candidate), cancellationToken)
                .ConfigureAwait(false);

            if (result is { } found && found.Step == candidate.Count - 1)
            {
                ops = candidate;
            }
        }

        return history.With(ops);
    }
}

/// <summary>Seed selection for oracle runs, read from the environment.</summary>
public static class MembershipOracleSeeds
{
    public const string SeedVariable = "COORDINATION_ORACLE_SEED";

    /// <summary>
    /// The replay seed alone when <c>COORDINATION_ORACLE_SEED</c> is set; otherwise <c>COORDINATION_ORACLE_SEEDS</c>
    /// seeds (default <paramref name="defaultCount" />) from <c>COORDINATION_ORACLE_FIRST_SEED</c> (default 1).
    /// </summary>
    public static IEnumerable<int> Resolve(int defaultCount)
    {
        if (_Read(SeedVariable) is { } single)
        {
            return [single];
        }

        return Enumerable.Range(
            _Read("COORDINATION_ORACLE_FIRST_SEED") ?? 1,
            _Read("COORDINATION_ORACLE_SEEDS") ?? defaultCount
        );
    }

    /// <summary>Appends <paramref name="line" /> to the file <c>COORDINATION_ORACLE_REPORT</c> names, when set.</summary>
    public static void WriteReport(string line)
    {
        if (Environment.GetEnvironmentVariable("COORDINATION_ORACLE_REPORT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static int? _Read(string name)
    {
        return int.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : null;
    }
}

/// <summary>
/// Differential check of a relational store against the in-memory model: generated histories run on both, step by
/// step, and any difference fails with the seed and a shrunk history that replays it.
/// </summary>
public abstract class MembershipOracleTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ICoordinationOracleFixture
{
    public virtual void should_generate_the_same_history_for_one_seed()
    {
        var first = MembershipOracleGenerator.Generate(42);
        var second = MembershipOracleGenerator.Generate(42);

        second.Nodes.Should().Equal(first.Nodes);
        second.Ops.Should().Equal(first.Ops);
    }

    public virtual async Task should_hide_retention_expired_rows_from_the_snapshot_before_they_are_pruned()
    {
        var scope = new MembershipOracleScope(new MembershipOracleHistory(Seed: 0, Nodes: ["node-a"], Ops: []));
        await using var session = await MembershipOracleSession.StartAsync(
            fixture,
            scope,
            pruneOnEverySnapshot: false,
            AbortToken
        );
        await session.ExecuteAsync(new MembershipOracleOp.Allocate(0), AbortToken);
        await session.ExecuteAsync(new MembershipOracleOp.Register(0, 0, 0), AbortToken);

        // The first snapshot prunes, so the production throttle skips the prune on the next one.
        var before = await session.ExecuteAsync(new MembershipOracleOp.ReadSnapshot(), AbortToken);
        before.Should().StartWith("snapshot:[n0@");

        // Past DeadThreshold + DeadRetentionWindow.
        await session.ExecuteAsync(new MembershipOracleOp.AdvanceTime(TimeSpan.FromSeconds(300)), AbortToken);
        var after = await session.ExecuteAsync(new MembershipOracleOp.ReadSnapshot(), AbortToken);

        after.Should().StartWith("snapshot:[] |", "a row past the retention cutoff is absent from the snapshot");
        after.Should().Contain("live[n0@", "the throttled prune has not deleted the row yet");
    }

    public virtual async Task should_match_the_model_at_every_step_of_generated_histories()
    {
        var divergences = new List<MembershipOracleDivergence>();
        var histories = 0;

        foreach (var seed in MembershipOracleSeeds.Resolve(defaultCount: 400))
        {
            histories++;

            if (
                await MembershipDifferentialOracle.CheckAsync(
                    fixture,
                    MembershipOracleGenerator.Generate(seed),
                    AbortToken
                ) is
                { } divergence
            )
            {
                divergences.Add(divergence);
            }
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{GetType().Name}: {histories} histories, {divergences.Count} divergent"
        );
        Logger.LogInformation("{OracleSummary}", summary);
        MembershipOracleSeeds.WriteReport(summary);

        divergences
            .Should()
            .BeEmpty(string.Join(Environment.NewLine, divergences.Take(3).Select(static d => d.Describe())));
    }
}
