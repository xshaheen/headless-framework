// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.K8s;
using Headless.Testing.Tests;

namespace Tests;

public sealed class K8sDiscoveryOptionsTests : TestBase
{
    [Fact]
    public void should_default_to_true_when_show_only_explicit_visible_nodes()
    {
        // given & when
        var options = new K8sDiscoveryOptions();

        // then
        options.ShowOnlyExplicitVisibleNodes.Should().BeTrue();
    }

    [Fact]
    public void should_have_default_configuration_when_k8s_client_config()
    {
        // given & when
        var options = new K8sDiscoveryOptions();

        // then
        options.K8sClientConfig.Should().NotBeNull();
    }

    [Fact]
    public void should_allow_setting_show_only_explicit_visible_nodes_to_false()
    {
        // given & when
        var options = new K8sDiscoveryOptions { ShowOnlyExplicitVisibleNodes = false };

        // then
        options.ShowOnlyExplicitVisibleNodes.Should().BeFalse();
    }

    [Fact]
    public void should_allow_custom_k8s_client_config()
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
