// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;
using Headless.Caching.Benchmarks.Scenarios;

namespace Tests;

public sealed class CacheBenchmarkClientFactoryTests
{
    [Fact]
    public void GetDescriptors_WithRedisFalse_ReturnsOnlyInProcessProviders()
    {
        var descriptors = CacheBenchmarkClientFactory.GetDescriptors(includeRedis: false);

        descriptors.Should().AllSatisfy(x => x.Backend.Should().Be(CacheBenchmarkBackend.InProcess));
        descriptors
            .Select(x => x.Id)
            .Should()
            .Contain([
                BenchmarkProviderIds.HeadlessInMemory,
                BenchmarkProviderIds.HeadlessHybrid,
                BenchmarkProviderIds.FusionMemory,
                BenchmarkProviderIds.FoundatioMemory,
                BenchmarkProviderIds.MicrosoftMemory,
                BenchmarkProviderIds.MicrosoftMemoryDistributed,
            ]);
    }

    [Fact]
    public void GetDescriptors_WithRedisTrue_IncludesRedisProviders()
    {
        var providerIds = CacheBenchmarkClientFactory.GetDescriptors(includeRedis: true).Select(x => x.Id);

        providerIds.Should().Contain(BenchmarkProviderIds.HeadlessRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.FusionRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.FusionRedisDistributed);
        providerIds.Should().Contain(BenchmarkProviderIds.FoundatioRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.MicrosoftRedisDistributed);
    }

    [Fact]
    public void MemoryOnlyProviderIds_ReturnsStandaloneMemoryProviders()
    {
        CacheBenchmarkClientFactory
            .MemoryOnlyProviderIds()
            .Should()
            .Equal(
                BenchmarkProviderIds.HeadlessInMemory,
                BenchmarkProviderIds.FusionMemory,
                BenchmarkProviderIds.FoundatioMemory,
                BenchmarkProviderIds.MicrosoftMemory
            );
    }

    [Fact]
    public void DistributedOnlyProviderIds_WithoutRedis_ReturnsDistributedContractBaseline()
    {
        CacheBenchmarkClientFactory
            .DistributedOnlyProviderIds(includeRedis: false)
            .Should()
            .Equal(BenchmarkProviderIds.MicrosoftMemoryDistributed);
    }

    [Fact]
    public void DistributedOnlyProviderIds_WithRedis_ReturnsStandaloneDistributedProviders()
    {
        CacheBenchmarkClientFactory
            .DistributedOnlyProviderIds(includeRedis: true)
            .Should()
            .Equal(
                BenchmarkProviderIds.HeadlessRedis,
                BenchmarkProviderIds.FusionRedisDistributed,
                BenchmarkProviderIds.FoundatioRedis,
                BenchmarkProviderIds.MicrosoftRedisDistributed
            );
    }

    [Fact]
    public void FeatureProviders_ExcludeProvidersWithoutFeatureSemantics()
    {
        var providerIds = BenchmarkScenarioSources.FeatureProviders().ToArray();

        providerIds.Should().Contain(BenchmarkProviderIds.HeadlessInMemory);
        providerIds.Should().Contain(BenchmarkProviderIds.FusionMemory);
        providerIds.Should().NotContain(BenchmarkProviderIds.FoundatioMemory);
        providerIds.Should().NotContain(BenchmarkProviderIds.MicrosoftMemory);
        providerIds.Should().NotContain(BenchmarkProviderIds.MicrosoftMemoryDistributed);
    }

    [Theory]
    [InlineData(BenchmarkProviderIds.HeadlessInMemory)]
    [InlineData(BenchmarkProviderIds.HeadlessHybrid)]
    [InlineData(BenchmarkProviderIds.FusionMemory)]
    [InlineData(BenchmarkProviderIds.FoundatioMemory)]
    [InlineData(BenchmarkProviderIds.MicrosoftMemory)]
    [InlineData(BenchmarkProviderIds.MicrosoftMemoryDistributed)]
    public async Task Create_InProcessProvider_RoundTripsPayload(string providerId)
    {
        await using var client = CacheBenchmarkClientFactory.Create(
            providerId,
            BenchmarkKeyPrefix.Create(providerId, "unit", Guid.NewGuid().ToString("N"))
        );
        var payload = BenchmarkPayloadFactory.Create(256, seed: 77);

        await client.SetAsync("roundtrip", payload, TimeSpan.FromMinutes(1), CancellationToken.None);
        var cached = await client.GetAsync("roundtrip", CancellationToken.None);
        await client.RemoveAsync("roundtrip", CancellationToken.None);
        var removed = await client.GetAsync("roundtrip", CancellationToken.None);

        cached.Should().NotBeNull();
        cached!.Text.Should().Be(payload.Text);
        cached.Bytes.Should().Equal(payload.Bytes);
        removed.Should().BeNull();
    }
}
