// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A host that requires a tenant on every publish still sends the release signal for a lock released from host scope,
/// such as a system job or the seed verb, so waiters on other nodes wake instead of waiting out their poll.
/// </summary>
public sealed class LockReleasedStrictTenancyTests : TestBase
{
    [Fact]
    public async Task should_publish_the_release_signal_from_host_scope_when_a_tenant_is_required_on_publish()
    {
        // given - required publish tenancy and no ambient tenant
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessDistributedLocks(setup => setup.UseInMemory().UseMessagingReleaseSignal());
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
        var locks = harness.ServiceProvider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var lease = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        lease.Should().NotBeNull();

        // when - the lock's own release path publishes the signal
        await locks.ReleaseAsync(resource, lease!.LeaseId, AbortToken);

        // then - the signal goes out tenantless, while an application publish without a tenant is still refused
        var published = await harness.WaitForPublishedAsync<DistributedLockReleased>(cancellationToken: AbortToken);
        published.Headers.Should().NotContainKey(Headers.TenantId);
        var applicationPublish = () =>
            harness.Publisher.PublishAsync(
                new DistributedLockReleased(resource, lease.LeaseId),
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
