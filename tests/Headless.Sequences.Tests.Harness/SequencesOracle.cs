// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Sequences;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>One increment of the counter at <see cref="Key" /> in the history's key pool.</summary>
public sealed record SequencesOracleIncrement(int Key, long InsertValue, long Delta);

/// <summary>A step of a generated history: one autonomous increment, or a unit of increments that commits or rolls back.</summary>
public sealed record SequencesOracleStep(IReadOnlyList<SequencesOracleIncrement> Increments, bool InUnit, bool Commits)
{
    public override string ToString()
    {
        var increments = string.Join(", ", Increments.Select(static i => $"k{i.Key}+{i.Delta}(start {i.InsertValue})"));

        return InUnit ? $"unit[{increments}] {(Commits ? "commit" : "rollback")}" : $"auto {increments}";
    }
}

/// <summary>A generated history: the key pool and the steps over it.</summary>
public sealed record SequencesOracleHistory(
    int Seed,
    IReadOnlyList<SequenceKey> Keys,
    IReadOnlyList<SequencesOracleStep> Steps
);

/// <summary>
/// Generates seeded counter histories over keys that stress what the engines compare: case, composition forms,
/// surrogate pairs, maximum lengths, and the empty partition and tenant.
/// </summary>
public static class SequencesOracleGenerator
{
    public static SequencesOracleHistory Generate(int seed, string runId)
    {
        var random = new Random(seed);
        var keys = Enumerable.Range(0, random.Next(2, 7)).Select(_ => _Key(random, runId)).ToList();
        var steps = new List<SequencesOracleStep>();

        for (var i = random.Next(5, 30); i > 0; i--)
        {
            var inUnit = random.Next(3) == 0;
            var increments = Enumerable
                .Range(0, inUnit ? random.Next(1, 4) : 1)
                .Select(_ => new SequencesOracleIncrement(
                    random.Next(keys.Count),
                    random.NextInt64(-1_000_000_000_000, 1_000_000_000_000),
                    random.Next(-1000, 1001)
                ))
                .ToList();

            steps.Add(new SequencesOracleStep(increments, inUnit, Commits: !inUnit || random.Next(4) != 0));
        }

        return new SequencesOracleHistory(seed, keys, steps);
    }

    private static SequenceKey _Key(Random random, string runId)
    {
        // Every history owns its names through the run id, so histories never share a counter. Within a history the
        // stems collide on purpose: case variants and the two forms of "café" are distinct keys on every engine.
        var stem = random.Next(7) switch
        {
            0 => "invoice",
            1 => "Invoice",
            2 => "caf\u00e9",
            3 => "cafe\u0301",
            4 => "\U0001F600\U0001F600",
            5 => "a\u200Bb",
            _ => new string('x', SequenceFieldLimits.NameMaxLength - runId.Length - 1),
        };
        var name = $"{runId}-{stem}";

        var partition = random.Next(3) switch
        {
            0 => "",
            1 => "2026",
            _ => new string('p', SequenceFieldLimits.PartitionMaxLength),
        };
        var tenant = random.Next(3) switch
        {
            0 => "",
            1 => "acme",
            _ => "ACME",
        };

        return new SequenceKey(tenant, name, partition);
    }
}

/// <summary>The reference behavior: a dictionary in which a unit's increments land only when the unit commits.</summary>
public sealed class SequencesOracleModel
{
    private readonly Dictionary<SequenceKey, long> _values = [];

    public IReadOnlyDictionary<SequenceKey, long> Values => _values;

    /// <summary>Applies the step and returns what each increment returns.</summary>
    public List<long> Apply(SequencesOracleHistory history, SequencesOracleStep step)
    {
        var staged = new Dictionary<SequenceKey, long>(_values);
        var returned = new List<long>();

        foreach (var increment in step.Increments)
        {
            var key = history.Keys[increment.Key];
            var value = staged.TryGetValue(key, out var stored) ? stored + increment.Delta : increment.InsertValue;
            staged[key] = value;
            returned.Add(value);
        }

        if (step.Commits)
        {
            foreach (var (key, value) in staged)
            {
                _values[key] = value;
            }
        }

        return returned;
    }
}

/// <summary>The first disagreement between an engine and the model, with the smallest history still showing one.</summary>
public sealed record SequencesOracleDivergence(int Seed, string Description, SequencesOracleHistory Shrunk)
{
    public override string ToString()
    {
        var steps = new StringBuilder();

        foreach (var step in Shrunk.Steps)
        {
            steps.Append("  ").AppendLine(step.ToString());
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"seed {Seed}: {Description}\nreplay with SEQUENCES_ORACLE_SEED={Seed}\nshrunk history ({Shrunk.Steps.Count} steps):\n{steps}"
        );
    }
}

/// <summary>Runs histories on an engine through the store seam and diffs every step against the model.</summary>
public static class SequencesDifferentialOracle
{
    /// <summary>The seeds to run: the replay seed alone when set, otherwise <paramref name="count" /> seeds from 1.</summary>
    public static IReadOnlyList<int> Seeds(int count)
    {
        var replay = Environment.GetEnvironmentVariable("SEQUENCES_ORACLE_SEED");

        return int.TryParse(replay, CultureInfo.InvariantCulture, out var seed)
            ? [seed]
            : [.. Enumerable.Range(1, count)];
    }

    public static async Task<SequencesOracleDivergence?> RunAsync(
        ISequencesFixture fixture,
        SequencesHost host,
        int seed,
        CancellationToken cancellationToken
    )
    {
        var history = SequencesOracleGenerator.Generate(seed, _RunId());
        var failure = await _RunHistoryAsync(fixture, host, history, cancellationToken).ConfigureAwait(false);

        if (failure is null)
        {
            return null;
        }

        // Greedy shrink: drop one step at a time while the history, rerun on fresh names, still disagrees.
        var steps = history.Steps.ToList();

        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var candidate = steps.Where((_, index) => index != i).ToList();
            var retry = SequencesOracleGenerator.Generate(seed, _RunId()) with { Steps = candidate };

            if (await _RunHistoryAsync(fixture, host, retry, cancellationToken).ConfigureAwait(false) is not null)
            {
                steps = candidate;
            }
        }

        return new SequencesOracleDivergence(seed, failure, history with { Steps = steps });
    }

    private static async Task<string?> _RunHistoryAsync(
        ISequencesFixture fixture,
        SequencesHost host,
        SequencesOracleHistory history,
        CancellationToken cancellationToken
    )
    {
        var store = host.Services.GetRequiredService<ISequenceStore>();
        var model = new SequencesOracleModel();

        for (var index = 0; index < history.Steps.Count; index++)
        {
            var step = history.Steps[index];
            var expected = model.Apply(history, step);
            var actual = new List<long>();

            if (step.InUnit)
            {
                await using var unit = await fixture.BeginUnitAsync(host, cancellationToken).ConfigureAwait(false);

                foreach (var increment in step.Increments)
                {
                    actual.Add(
                        await store
                            .IncrementEnlistedAsync(
                                unit.Unit,
                                history.Keys[increment.Key],
                                increment.InsertValue,
                                increment.Delta,
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                    );
                }

                if (step.Commits)
                {
                    await unit.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await unit.RollbackAsync().ConfigureAwait(false);
                }
            }
            else
            {
                var increment = step.Increments[0];
                actual.Add(
                    await store
                        .IncrementAsync(
                            history.Keys[increment.Key],
                            increment.InsertValue,
                            increment.Delta,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                );
            }

            if (!actual.SequenceEqual(expected))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"step {index} ({step}) returned [{string.Join(", ", actual)}], the model [{string.Join(", ", expected)}]"
                );
            }
        }

        for (var key = 0; key < history.Keys.Count; key++)
        {
            var stored = await fixture.ReadValueAsync(history.Keys[key], cancellationToken).ConfigureAwait(false);
            long? expected = model.Values.TryGetValue(history.Keys[key], out var value) ? value : null;

            if (stored != expected)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"after the history, key {key} stores {stored?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}, the model {expected?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}"
                );
            }
        }

        return null;
    }

    private static string _RunId() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>The oracle scenarios each engine's leaf runs.</summary>
public abstract class SequencesOracleTests : Headless.Testing.Tests.TestBase
{
    protected abstract ISequencesFixture Fixture { get; }

    public virtual void should_generate_the_same_history_for_one_seed()
    {
        var first = SequencesOracleGenerator.Generate(42, "run");
        var second = SequencesOracleGenerator.Generate(42, "run");

        second.Keys.Should().Equal(first.Keys);
        second.Steps.Select(static s => s.ToString()).Should().Equal(first.Steps.Select(static s => s.ToString()));
    }

    public virtual async Task should_match_the_model_at_every_step_of_generated_histories()
    {
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var divergences = new List<SequencesOracleDivergence>();

        foreach (var seed in SequencesDifferentialOracle.Seeds(400))
        {
            if (await SequencesDifferentialOracle.RunAsync(Fixture, host, seed, AbortToken) is { } divergence)
            {
                divergences.Add(divergence);
            }
        }

        divergences.Should().BeEmpty(string.Join('\n', divergences.Take(3)));
    }
}
