// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.K8s;
using Headless.Testing.Tests;

namespace Tests;

public sealed class K8sDiscoveryOptionsTests : TestBase
{
    [Fact]
    public void ShowOnlyExplicitVisibleNodes_should_default_to_true()
    {
        // given & when
        var options = new K8sDiscoveryOptions();

        // then
        options.ShowOnlyExplicitVisibleNodes.Should().BeTrue();
    }

    [Fact]
    public void K8sClientConfig_should_have_default_configuration()
    {
        // given & when
        var options = new K8sDiscoveryOptions();

        // then
        options.K8sClientConfig.Should().NotBeNull();
    }

    [Fact]
    public void should_allow_setting_ShowOnlyExplicitVisibleNodes_to_false()
    {
        // given & when
        var options = new K8sDiscoveryOptions { ShowOnlyExplicitVisibleNodes = false };

        // then
        options.ShowOnlyExplicitVisibleNodes.Should().BeFalse();
    }

    [Fact]
    public void should_allow_custom_K8sClientConfig()
    {
        // given
        var customConfig = new k8s.KubernetesClientConfiguration
        {
            Namespace = "custom-namespace",
            Host = "https://custom-host:6443",
        };

        // when
        var options = new K8sDiscoveryOptions { K8sClientConfig = customConfig };

        // then
        options.K8sClientConfig.Should().BeSameAs(customConfig);
        options.K8sClientConfig.Namespace.Should().Be("custom-namespace");
        options.K8sClientConfig.Host.Should().Be("https://custom-host:6443");
    }
}
