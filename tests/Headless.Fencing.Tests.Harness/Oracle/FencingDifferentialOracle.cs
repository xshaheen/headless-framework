// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;

namespace Tests;

/// <summary>The first step at which a provider disagreed with the model, with the smallest history found that still shows it.</summary>
public sealed record FencingOracleDivergence(
    FencingOracleHistory History,
    int Step,
    FencingOracleObservation Model,
    FencingOracleObservation Actual,
    FencingOracleHistory Minimal,
    bool ReproducedOnReplay
)
{
    /// <summary>Groups divergences that are one defect: the operation kind and the provider's outcome, without the rows.</summary>
    public string Signature
    {
        get
        {
            var op = Minimal.Ops[^1].GetType().Name;
            var actual = Actual.Summary.Split(" | ", 2)[0];
            var model = Model.Summary.Split(" | ", 2)[0];

            // Generation ordinals and key indexes differ between histories that hit the same defect.
            return _Mask($"{op}: model {model} / provider {actual}");
        }
    }

    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"seed {History.Seed}: diverged at step {Step} ({History.Ops[Step]})"
        );
        builder.AppendLine(CultureInfo.InvariantCulture, $"  model:    {Model}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  provider: {Actual}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  replayed identically: {ReproducedOnReplay}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  minimal history ({Minimal.Ops.Count} ops):");
        builder.AppendLine(Minimal.Describe());

        return builder.ToString();
    }

    private static string _Mask(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            builder.Append(value[i]);

            if (
                value[i] is 'g' or 'k'
                && i + 1 < value.Length
                && char.IsAsciiDigit(value[i + 1])
                && (i == 0 || !char.IsAsciiLetter(value[i - 1]))
            )
            {
                builder.Append('#');

                while (i + 1 < value.Length && char.IsAsciiDigit(value[i + 1]))
                {
                    i++;
                }
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Runs generated histories against the in-memory model and a provider, step by step, and reports where they differ.
/// </summary>
public static class FencingDifferentialOracle
{
    // Bounds the reruns minimization may spend on one divergence.
    private const int _MaxShrinkRuns = 80;

    /// <summary>Runs <paramref name="history" /> on both fixtures and returns the first differing step, or <see langword="null" />.</summary>
    public static async Task<(
        int Step,
        FencingOracleObservation Model,
        FencingOracleObservation Actual
    )?> FindFirstDivergenceAsync(
        ILeasesFixture model,
        ILeasesFixture actual,
        FencingOracleHistory history,
        long precisionTicks,
        CancellationToken cancellationToken
    )
    {
        var runId = "o" + Guid.NewGuid().ToString("N")[..12];
        await using var modelSession = await FencingOracleSession
            .StartAsync(model, history, runId, cancellationToken)
            .ConfigureAwait(false);
        await using var actualSession = await FencingOracleSession
            .StartAsync(actual, history, runId, cancellationToken)
            .ConfigureAwait(false);

        for (var step = 0; step < history.Ops.Count; step++)
        {
            var expected = await modelSession.ExecuteAsync(history.Ops[step], cancellationToken).ConfigureAwait(false);
            var observed = await actualSession.ExecuteAsync(history.Ops[step], cancellationToken).ConfigureAwait(false);

            if (!expected.Matches(observed, precisionTicks))
            {
                return (step, expected, observed);
            }
        }

        return null;
    }

    /// <summary>Checks one history; on divergence, shrinks it and replays the shrunk history to prove it is deterministic.</summary>
    public static async Task<FencingOracleDivergence?> CheckAsync(
        ILeasesFixture model,
        ILeasesFixture actual,
        FencingOracleHistory history,
        long precisionTicks,
        CancellationToken cancellationToken
    )
    {
        var first = await FindFirstDivergenceAsync(model, actual, history, precisionTicks, cancellationToken)
            .ConfigureAwait(false);

        if (first is not { } found)
        {
            return null;
        }

        var minimal = await _ShrinkAsync(
                model,
                actual,
                history.With([.. history.Ops.Take(found.Step + 1)]),
                precisionTicks,
                cancellationToken
            )
            .ConfigureAwait(false);
        var replay = await FindFirstDivergenceAsync(model, actual, minimal, precisionTicks, cancellationToken)
            .ConfigureAwait(false);

        return new FencingOracleDivergence(
            history,
            found.Step,
            found.Model,
            found.Actual,
            minimal,
            replay is { } again && again.Step == minimal.Ops.Count - 1
        );
    }

    /// <summary>Checks every seed and returns each divergence found.</summary>
    public static async Task<IReadOnlyList<FencingOracleDivergence>> CheckSeedsAsync(
        ILeasesFixture model,
        ILeasesFixture actual,
        IEnumerable<int> seeds,
        int length,
        bool edgeKeys,
        long precisionTicks,
        CancellationToken cancellationToken
    )
    {
        var divergences = new List<FencingOracleDivergence>();

        foreach (var seed in seeds)
        {
            var history = FencingOracleGenerator.Generate(seed, length, edgeKeys);

            if (
                await CheckAsync(model, actual, history, precisionTicks, cancellationToken).ConfigureAwait(false) is
                { } divergence
            )
            {
                divergences.Add(divergence);
            }
        }

        return divergences;
    }

    /// <summary>Renders divergences grouped by signature, each group with its smallest reproduction and every seed.</summary>
    public static string Report(IReadOnlyList<FencingOracleDivergence> divergences)
    {
        var builder = new StringBuilder();

        foreach (var group in divergences.GroupBy(static d => d.Signature, StringComparer.Ordinal))
        {
            var smallest = group.OrderBy(static d => d.Minimal.Ops.Count).First();
            builder.AppendLine(CultureInfo.InvariantCulture, $"=== {group.Key}");
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"    seeds: {string.Join(",", group.Select(static d => d.History.Seed))}"
            );
            builder.AppendLine(smallest.Describe());
        }

        return builder.ToString();
    }

    private static async Task<FencingOracleHistory> _ShrinkAsync(
        ILeasesFixture model,
        ILeasesFixture actual,
        FencingOracleHistory history,
        long precisionTicks,
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

            var result = await FindFirstDivergenceAsync(
                    model,
                    actual,
                    history.With(candidate),
                    precisionTicks,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (result is { } found && found.Step == candidate.Count - 1)
            {
                ops = candidate;
            }
        }

        return history.With(ops);
    }
}
