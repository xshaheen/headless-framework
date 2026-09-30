// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The oracle's own ground truth, in unit CI: the same seed yields the same history, and two independent in-memory
/// stores observe every generated history identically. A failure here means the oracle, not a provider, is
/// nondeterministic, and no provider verdict it gives can be trusted.
/// </summary>
#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public sealed class InMemoryOracleModelTests : TestBase
{
    [Fact]
    public void should_generate_the_same_history_from_the_same_seed()
    {
        foreach (var seed in Enumerable.Range(1, 50))
        {
            FencingOracleGenerator
                .Generate(seed)
                .Describe()
                .Should()
                .Be(FencingOracleGenerator.Generate(seed).Describe());
        }

        FencingOracleGenerator.Generate(1).Describe().Should().NotBe(FencingOracleGenerator.Generate(2).Describe());
    }

    [Fact]
    public async Task should_observe_every_generated_history_identically_on_two_independent_models()
    {
        using var model = new InMemoryLeasesFixture();
        using var replica = new InMemoryLeasesFixture();

        var divergences = await FencingDifferentialOracle.CheckSeedsAsync(
            model,
            replica,
            OracleSeeds.Resolve(defaultCount: 200),
            length: 60,
            edgeKeys: true,
            precisionTicks: 1,
            AbortToken
        );

        divergences.Should().BeEmpty(FencingDifferentialOracle.Report(divergences));
    }
}
