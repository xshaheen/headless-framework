// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.K8s;
using Headless.Messaging.Dashboard.NodeDiscovery;

namespace Tests;

public sealed class K8sDiscoveryOptionsExtensionsTests : TestBase
{
    [Fact]
    public void UseK8sDiscovery_should_return_same_MessagingOptions_instance()
    {
        // given
        var messagingOptions = new MessagingOptions();

        // when
        var result = messagingOptions.UseK8sDiscovery();

        // then
        result.Should().BeSameAs(messagingOptions);
    }

    [Fact]
    public void UseK8sDiscovery_with_options_should_return_same_MessagingOptions_instance()
    {
        // given
        var messagingOptions = new MessagingOptions();

        // when
        var result = messagingOptions.UseK8sDiscovery(opt => opt.ShowOnlyExplicitVisibleNodes = false);

        // then
        result.Should().BeSameAs(messagingOptions);
    }

    [Fact]
    public void UseK8sDiscovery_should_throw_when_options_action_is_null()
    {
        // given
        var messagingOptions = new MessagingOptions();

        // when
        var act = () => messagingOptions.UseK8sDiscovery(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseK8sDiscovery_should_register_extension()
    {
        // given
        var messagingOptions = new MessagingOptions();

        // when
        messagingOptions.UseK8sDiscovery();

        // then - verify extension was registered via internal property
        var extensionsProp = typeof(MessagingOptions).GetProperty(
            "Extensions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        var extensions = extensionsProp?.GetValue(messagingOptions) as IList<IMessagesOptionsExtension>;

        extensions.Should().NotBeNullOrEmpty();
        extensions.Should().ContainSingle(x => x.GetType().Name == "K8sDiscoveryOptionsExtension");
    }
}

