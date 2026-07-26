// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.K8s;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class K8sDiscoveryOptionsExtensionsTests : TestBase
{
    [Fact]
    public void should_return_same_setup_instance_when_use_k8s_discovery()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseK8sDiscovery();

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_return_same_setup_instance_when_use_k8s_discovery_with_options()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseK8sDiscovery(opt => opt.ShowOnlyExplicitVisibleNodes = false);

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_throw_when_use_k8s_discovery_options_action_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UseK8sDiscovery(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_register_extension_when_use_k8s_discovery()
    {
        // given
        var setup = _CreateSetup();

        // when
        setup.UseK8sDiscovery();

        // then - verify extension was registered via internal property
        var extensionsProp = typeof(MessagingSetupBuilder).GetProperty(
            nameof(MessagingSetupBuilder.Extensions),
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        var extensions = extensionsProp?.GetValue(setup) as IList<IMessagesOptionsExtension>;

        extensions.Should().NotBeNullOrEmpty();
        extensions.Should().ContainSingle(x => x.GetType().Name == "K8sDiscoveryOptionsExtension");
    }

    [Fact]
    public void should_preserve_k8s_discovery_name_without_contributing_provider_capabilities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseK8sDiscovery());

        using var provider = services.BuildServiceProvider();

        typeof(MessagingK8sDiscoveryOptionsExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should()
            .Contain(method => method.Name == "UseK8sDiscovery");
        provider
            .GetRequiredService<IMessagingCapabilityModel>()
            .DeclaredCapabilities.Should()
            .BeEmpty("node discovery is cross-cutting and is not a transport, storage, or coordination provider");
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());
    }
}
