// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;
using Headless.Caching.Benchmarks.Scenarios;

namespace Tests;

public sealed class CacheBenchmarkClientFactoryTests
{
    [Fact]
    public void get_descriptors_with_redis_false_returns_only_in_process_providers()
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
    public void get_descriptors_with_redis_true_includes_redis_providers()
    {
        var providerIds = CacheBenchmarkClientFactory.GetDescriptors(includeRedis: true).Select(x => x.Id);

        providerIds.Should().Contain(BenchmarkProviderIds.HeadlessRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.FusionRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.FusionRedisDistributed);
        providerIds.Should().Contain(BenchmarkProviderIds.FoundatioRedis);
        providerIds.Should().Contain(BenchmarkProviderIds.MicrosoftRedisDistributed);
    }

    [Fact]
    public void memory_only_provider_ids_returns_standalone_memory_providers()
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
    public void distributed_only_provider_ids_without_redis_returns_distributed_contract_baseline()
    {
        CacheBenchmarkClientFactory
            .DistributedOnlyProviderIds(includeRedis: false)
            .Should()
            .Equal(BenchmarkProviderIds.MicrosoftMemoryDistributed);
    }

    [Fact]
    public void distributed_only_provider_ids_with_redis_returns_standalone_distributed_providers()
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
    public void feature_providers_exclude_providers_without_feature_semantics()
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
    public async Task create_in_process_provider_round_trips_payload(string providerId)
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
