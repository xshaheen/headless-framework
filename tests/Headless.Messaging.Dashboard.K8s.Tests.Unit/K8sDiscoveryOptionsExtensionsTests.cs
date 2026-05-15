// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.K8s;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class K8SDiscoveryOptionsExtensionsTests : TestBase
{
    [Fact]
    public void UseK8sDiscovery_should_return_same_setup_instance()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseK8sDiscovery();

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void UseK8sDiscovery_with_options_should_return_same_setup_instance()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseK8sDiscovery(opt => opt.ShowOnlyExplicitVisibleNodes = false);

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void UseK8sDiscovery_should_throw_when_options_action_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UseK8sDiscovery(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseK8sDiscovery_should_register_extension()
    {
        // given
        var setup = _CreateSetup();

        // when
        setup.UseK8sDiscovery();

        // then - verify extension was registered via internal property
        var extensionsProp = typeof(MessagingSetupBuilder).GetProperty(
            "Extensions",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        var extensions = extensionsProp?.GetValue(setup) as IList<IMessagesOptionsExtension>;

        extensions.Should().NotBeNullOrEmpty();
        extensions.Should().ContainSingle(x => x.GetType().Name == "K8sDiscoveryOptionsExtension");
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());
    }
}
