// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Context;
using Headless.Features;
using Headless.Features.Resources;
using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Values;

/// <summary>
/// A host that requires a tenant on every publish still announces a feature change made from host scope, such as an
/// edition or host-level write, so peers drop their cached value.
/// </summary>
public sealed class FeatureManagerStrictTenancyTests : TestBase
{
    [Fact]
    public async Task should_announce_a_host_scope_change_when_a_tenant_is_required_on_publish()
    {
        // given - required publish tenancy and no ambient tenant
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.ConfigureMessaging(static messaging =>
                    messaging.Message<FeatureChangedMessage>(FeatureChangedMessage.MessageName, "1")
                );
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
        const string featureName = "Reports";
        var definitionManager = Substitute.For<IFeatureDefinitionManager>();
        definitionManager.FindAsync(featureName, AbortToken).Returns(new FeatureDefinition(featureName));
        var provider = Substitute.For<IFeatureValueProvider>();
        provider.Name.Returns("Edition");
        var valueProviderManager = Substitute.For<IFeatureValueProviderManager>();
        valueProviderManager.ValueProviders.Returns([provider]);
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("node-a");
        var sut = new FeatureManager(
            definitionManager,
            valueProviderManager,
            new DefaultFeatureErrorsDescriptor(),
            hostIdentity,
            Substitute.For<ICurrentTenant>(),
            harness.Publisher
        );

        // when
        await sut.SetAsync(featureName, "true", "Edition", "pro", forceToSet: true, cancellationToken: AbortToken);

        // then - the announcement goes out tenantless, while an application publish without a tenant is still refused
        var published = await harness.WaitForPublishedAsync<FeatureChangedMessage>(cancellationToken: AbortToken);
        published.Headers.Should().NotContainKey(Headers.TenantId);
        var applicationPublish = () =>
            harness.Publisher.PublishAsync(
                new FeatureChangedMessage
                {
                    FeatureNames = [featureName],
                    ProviderName = "Edition",
                    OriginHostName = "node-a",
                },
                cancellationToken: AbortToken
            );
        await applicationPublish.Should().ThrowAsync<MissingTenantContextException>();
    }

    // RequireTenantOnPublish() is the public switch, but it hangs off the host-builder tenancy seam, and the option's
    // setter is internal, so set it the way that seam's PostConfigure does.
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
