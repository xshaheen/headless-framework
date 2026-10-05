// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The invalidation travels under the name every hybrid registration declares, so replicas and services sharing a
/// broker agree on the topic whatever naming conventions each host configures for its own messages.
/// </summary>
public sealed class HybridCacheInvalidationContractTests : TestBase
{
    [Fact]
    public async Task should_publish_and_consume_under_the_declared_name_whatever_the_host_conventions()
    {
        // given - conventions that rename every message the host does not declare
        using var l2 = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddHeadlessCaching(setup =>
                {
                    setup.AddMemoryTier();
                    setup.RegisterTierProvider(
                        CacheConstants.RemoteCacheProvider,
                        svc =>
                        {
                            var remote = new InMemoryRemoteCacheAdapter(l2);
                            svc.AddSingleton<IRemoteCache>(remote);
                            svc.AddKeyedSingleton<ICache>(CacheConstants.RemoteCacheProvider, remote);
                        }
                    );
                    setup.UseHybrid();
                });
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                    setup.UseConventions(static conventions =>
                        conventions
                            .UseKebabCaseMessageNames()
                            .WithMessageNamePrefix("app.")
                            .WithMessageNameSuffix(".event")
                    );
                });
            },
            AbortToken
        );

        // when - the cache's own write path publishes the invalidation
        await harness
            .ServiceProvider.GetRequiredService<HybridCache>()
            .UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        var published = await harness.WaitForPublishedAsync<CacheInvalidationMessage>(cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumedAsync<CacheInvalidationMessage>(cancellationToken: AbortToken);
        published.MessageName.Should().Be("headless.caching.hybrid.invalidation");
        consumed.MessageName.Should().Be("headless.caching.hybrid.invalidation");
        harness
            .ServiceProvider.GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(metadata => metadata.ConsumerType == typeof(HybridCacheInvalidationConsumer))
            .Which.MessageName.Should()
            .Be("headless.caching.hybrid.invalidation");
    }
}
