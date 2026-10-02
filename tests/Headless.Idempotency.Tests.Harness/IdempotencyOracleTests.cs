// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Differential check of a provider against the in-memory model: generated histories run on both, step by step, and
/// any difference fails with the seed and a shrunk history that replays it.
/// </summary>
/// <remarks>
/// Replay one seed with <c>IDEMPOTENCY_ORACLE_SEED=&lt;seed&gt;</c>. <c>IDEMPOTENCY_ORACLE_SEEDS</c> sets how many
/// seeds a run checks (default 25), <c>IDEMPOTENCY_ORACLE_FIRST_SEED</c> the first one (default 1), and
/// <c>IDEMPOTENCY_ORACLE_REPORT</c> a file the grouped report is also written to.
/// <c>IDEMPOTENCY_ORACLE_PRECISION</c> overrides the timestamp tolerance in ticks, to measure raw precision drift.
/// </remarks>
public abstract class IdempotencyOracleTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IIdempotencyFixture
{
    /// <summary>
    /// The tolerance, in ticks, within which a stored retention-minus-lease counts as the model's. Exact: call
    /// validation truncates every duration to whole microseconds, the finest resolution every provider stores.
    /// </summary>
    protected virtual long PrecisionTicks => 1;

    /// <summary>
    /// The real time waited before each operation that writes an instant; longer than the coarsest database clock step
    /// measured (about 12 milliseconds on Azure SQL Edge).
    /// </summary>
    protected virtual TimeSpan ClockSpacing => TimeSpan.FromMilliseconds(20);

    protected TFixture Fixture { get; } = fixture;

    /// <summary>Every edge-case key: case, accent composition, control characters, surrogates, the longest value.</summary>
    public virtual Task should_match_the_in_memory_model_on_generated_histories()
    {
        return _CheckAsync(edgeKeys: true, "edge");
    }

    /// <summary>Only keys every provider can store, so a key-shape divergence cannot hide a behavioral one.</summary>
    public virtual Task should_match_the_in_memory_model_on_ordinary_keys()
    {
        return _CheckAsync(edgeKeys: false, "ordinary");
    }

    private async Task _CheckAsync(bool edgeKeys, string label)
    {
        using var model = new InMemoryIdempotencyFixture();

        var divergences = await IdempotencyDifferentialOracle.CheckSeedsAsync(
            model,
            Fixture,
            IdempotencyOracleSeeds.Resolve(),
            length: 40,
            edgeKeys,
            new IdempotencyOracleTolerance(IdempotencyOracleSeeds.PrecisionOverride ?? PrecisionTicks, ClockSpacing),
            AbortToken
        );

        var report = IdempotencyDifferentialOracle.Report(divergences);
        IdempotencyOracleSeeds.WriteReport($"{GetType().Name}.{label}", report);

        divergences
            .Should()
            .BeEmpty(
                $"the provider must behave as the in-memory model; replay a seed with IDEMPOTENCY_ORACLE_SEED{Environment.NewLine}{report}"
            );
    }
}

/// <summary>Seed selection and report output for oracle runs, read from the environment.</summary>
public static class IdempotencyOracleSeeds
{
    public static IEnumerable<int> Resolve(int defaultCount = 25)
    {
        if (_Read("IDEMPOTENCY_ORACLE_SEED") is { } single)
        {
            return [single];
        }

        var first = _Read("IDEMPOTENCY_ORACLE_FIRST_SEED") ?? 1;
        var count = _Read("IDEMPOTENCY_ORACLE_SEEDS") ?? defaultCount;

        return Enumerable.Range(first, count);
    }

    /// <summary>A timestamp tolerance in ticks that replaces the provider's own, or <see langword="null" />.</summary>
    public static long? PrecisionOverride => _Read("IDEMPOTENCY_ORACLE_PRECISION");

    public static void WriteReport(string label, string report)
    {
        if (Environment.GetEnvironmentVariable("IDEMPOTENCY_ORACLE_REPORT") is not { Length: > 0 } path)
        {
            return;
        }

        File.AppendAllText(
            path,
            $"##### {label}{Environment.NewLine}{(report.Length == 0 ? "no divergence" + Environment.NewLine : report)}"
        );
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
