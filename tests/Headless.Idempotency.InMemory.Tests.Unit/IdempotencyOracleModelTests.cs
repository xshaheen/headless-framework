// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The oracle's own ground truth, in unit CI: the same seed yields the same history, and two independent in-memory
/// stores observe every generated history identically. A failure here means the oracle, not a provider, is
/// nondeterministic, and no provider verdict it gives can be trusted.
/// </summary>
public sealed class IdempotencyOracleModelTests : TestBase
{
    [Fact]
    public void should_generate_the_same_history_from_the_same_seed()
    {
        foreach (var seed in Enumerable.Range(1, 50))
        {
            IdempotencyOracleGenerator
                .Generate(seed)
                .Describe()
                .Should()
                .Be(IdempotencyOracleGenerator.Generate(seed).Describe());
        }

        IdempotencyOracleGenerator
            .Generate(1)
            .Describe()
            .Should()
            .NotBe(IdempotencyOracleGenerator.Generate(2).Describe());
    }

    [Fact]
    public async Task should_observe_every_generated_history_identically_on_two_independent_models()
    {
        using var model = new InMemoryIdempotencyFixture();
        using var replica = new InMemoryIdempotencyFixture();

        var divergences = await IdempotencyDifferentialOracle.CheckSeedsAsync(
            model,
            replica,
            IdempotencyOracleSeeds.Resolve(defaultCount: 200),
            length: 60,
            edgeKeys: true,
            IdempotencyOracleTolerance.Exact,
            AbortToken
        );

        divergences.Should().BeEmpty(IdempotencyDifferentialOracle.Report(divergences));
    }
}
