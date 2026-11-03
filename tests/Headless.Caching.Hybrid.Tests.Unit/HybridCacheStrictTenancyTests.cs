// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Caching;
using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A host that requires a tenant on every publish still sends the invalidations its cache writes from host scope, such
/// as a tenant-catalog lookup before authentication or a host-level write, so peers drop their stale L1 copies.
/// </summary>
public sealed class HybridCacheStrictTenancyTests : TestBase
{
    [Fact]
    public async Task should_publish_the_invalidation_from_host_scope_when_a_tenant_is_required_on_publish()
    {
        // given - required publish tenancy and no ambient tenant
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
                    setup.UseHybrid(options => options.ReThrowBackplaneExceptions = true);
                });
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
                });
                _RequireTenantOnPublish(services);
            },
            AbortToken
        );

        // when - the cache's own write path publishes the invalidation
        await harness
            .ServiceProvider.GetRequiredService<HybridCache>()
            .UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then - the invalidation goes out tenantless, while an application publish without a tenant is still refused
        var published = await harness.WaitForPublishedAsync<CacheInvalidationMessage>(cancellationToken: AbortToken);
        published.Headers.Should().NotContainKey(Headers.TenantId);
        var applicationPublish = () =>
            harness.Publisher.PublishAsync(
                new CacheInvalidationMessage { InstanceId = "app", Key = "key" },
                cancellationToken: AbortToken
            );
        await applicationPublish.Should().ThrowAsync<MissingTenantContextException>();
    }

    // RequireTenantOnPublish() is the public switch, but it hangs off the host-builder tenancy seam this project does
    // not reference, and the option's setter is internal, so set it the way that seam's PostConfigure does.
    private static void _RequireTenantOnPublish(IServiceCollection services)
    {
        services.PostConfigure<MessagingOptions>(static options =>
            typeof(MessagingOptions)
                .GetProperty(
                    nameof(MessagingOptions.TenantContextRequired),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )!
                .SetValue(options, true)
        );
    }
}
