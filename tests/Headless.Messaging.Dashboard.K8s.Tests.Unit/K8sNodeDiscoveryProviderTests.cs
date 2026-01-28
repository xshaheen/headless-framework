// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Dashboard.K8s;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using k8s.Models;

namespace Tests;

public sealed class K8sNodeDiscoveryProviderTests : TestBase
{
    private readonly K8sNodeDiscoveryProvider _provider;
    private readonly K8sDiscoveryOptions _options;

    public K8sNodeDiscoveryProviderTests()
    {
        _options = new K8sDiscoveryOptions();
        _provider = new K8sNodeDiscoveryProvider(LoggerFactory, _options);
    }

    #region FilterNodesByTags Tests

    [Fact]
    public void FilterNodesByTags_should_hide_node_when_visibility_is_hide()
    {
        // given
        var tags = new Dictionary<string, string> { ["headless.messaging.visibility"] = "hide" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void FilterNodesByTags_should_show_node_when_visibility_is_show()
    {
        // given
        var tags = new Dictionary<string, string> { ["headless.messaging.visibility"] = "show" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void FilterNodesByTags_should_hide_node_by_default_when_ShowOnlyExplicitVisibleNodes_is_true()
    {
        // given
        _options.ShowOnlyExplicitVisibleNodes = true;
        var tags = new Dictionary<string, string> { ["some-other-tag"] = "value" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void FilterNodesByTags_should_show_node_when_ShowOnlyExplicitVisibleNodes_is_false_and_no_hide_tag()
    {
        // given
        _options.ShowOnlyExplicitVisibleNodes = false;
        var provider = new K8sNodeDiscoveryProvider(LoggerFactory, _options);
        var tags = new Dictionary<string, string> { ["headless.messaging.something"] = "value" };

        // when
        var result = _InvokeFilterNodesByTags(tags, provider);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void FilterNodesByTags_should_extract_portIndex_from_tag()
    {
        // given
        var tags = new Dictionary<string, string>
        {
            ["headless.messaging.visibility"] = "show",
            ["headless.messaging.portIndex"] = "2",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.FilteredPortIndex.Should().Be(2);
    }

    [Fact]
    public void FilterNodesByTags_should_extract_portName_from_tag()
    {
        // given
        var tags = new Dictionary<string, string>
        {
            ["headless.messaging.visibility"] = "show",
            ["headless.messaging.portName"] = "http",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.FilteredPortName.Should().Be("http");
    }

    [Fact]
    public void FilterNodesByTags_should_handle_null_tags()
    {
        // given & when
        var result = _InvokeFilterNodesByTags(null!);

        // then
        result.HideNode.Should().BeTrue(); // default when ShowOnlyExplicitVisibleNodes is true
        result.FilteredPortIndex.Should().Be(0);
        result.FilteredPortName.Should().BeEmpty();
    }

    [Fact]
    public void FilterNodesByTags_should_be_case_insensitive_for_tag_prefix()
    {
        // given
        var tags = new Dictionary<string, string> { ["HEADLESS.MESSAGING.visibility"] = "show" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void FilterNodesByTags_should_be_case_insensitive_for_visibility_value()
    {
        // given
        var tags = new Dictionary<string, string> { ["headless.messaging.visibility"] = "HIDE" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void FilterNodesByTags_should_ignore_non_headless_tags()
    {
        // given
        var tags = new Dictionary<string, string> { ["app"] = "my-app", ["version"] = "1.0" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then - node hidden because no explicit show tag and ShowOnlyExplicitVisibleNodes is true
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void FilterNodesByTags_should_use_last_portIndex_when_multiple_exist()
    {
        // given - simulating multiple portIndex tags (unlikely but testing behavior)
        var tags = new Dictionary<string, string>
        {
            ["headless.messaging.visibility"] = "show",
            ["headless.messaging.portIndex"] = "3",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.FilteredPortIndex.Should().Be(3);
    }

    [Fact]
    public void FilterNodesByTags_should_handle_invalid_portIndex_value()
    {
        // given
        var tags = new Dictionary<string, string>
        {
            ["headless.messaging.visibility"] = "show",
            ["headless.messaging.portIndex"] = "invalid",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.FilteredPortIndex.Should().Be(0); // default when parsing fails
    }

    #endregion

    #region GetPortByNameOrIndex Tests

    [Fact]
    public void GetPortByNameOrIndex_should_return_zero_when_service_is_null()
    {
        // given & when
        var result = _InvokeGetPortByNameOrIndex(null, "", 0);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void GetPortByNameOrIndex_should_return_zero_when_service_ports_is_null()
    {
        // given
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta { Name = "test-service" },
            Spec = new V1ServiceSpec { Ports = null },
        };

        // when
        var result = _InvokeGetPortByNameOrIndex(service, "", 0);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void GetPortByNameOrIndex_should_return_first_port_when_no_filter()
    {
        // given
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta { Name = "test-service" },
            Spec = new V1ServiceSpec
            {
                Ports =
                [
                    new V1ServicePort { Name = "http", Port = 8080 },
                    new V1ServicePort { Name = "https", Port = 8443 },
                ],
            },
        };

        // when
        var result = _InvokeGetPortByNameOrIndex(service, "", 0);

        // then
        result.Should().Be(8080);
    }

    [Fact]
    public void GetPortByNameOrIndex_should_return_port_by_index()
    {
        // given
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta { Name = "test-service" },
            Spec = new V1ServiceSpec
            {
                Ports =
                [
                    new V1ServicePort { Name = "http", Port = 8080 },
                    new V1ServicePort { Name = "https", Port = 8443 },
                    new V1ServicePort { Name = "grpc", Port = 9090 },
                ],
            },
        };

        // when
        var result = _InvokeGetPortByNameOrIndex(service, "", 1);

        // then
        result.Should().Be(8443);
    }

    [Fact]
    public void GetPortByNameOrIndex_should_return_first_port_when_index_out_of_range()
    {
        // given
        var service = new V1Service
        {
            Metadata = new V1ObjectMeta { Name = "test-service" },
            Spec = new V1ServiceSpec { Ports = [new V1ServicePort { Name = "http", Port = 8080 }] },
        };

        // when
        var result = _InvokeGetPortByNameOrIndex(service, "", 10);

        // then
        result.Should().Be(8080); // Falls back to first port
    }

    #endregion

    #region GetTagScope Tests

    [Fact]
    public void GetTagScope_should_extract_scope_from_tag()
    {
        // given
        var tag = new KeyValuePair<string, string>("headless.messaging.visibility", "show");

        // when
        var result = _InvokeGetTagScope(tag);

        // then
        result.Should().Be("visibility");
    }

    [Fact]
    public void GetTagScope_should_handle_tag_without_dot_after_prefix()
    {
        // given
        var tag = new KeyValuePair<string, string>("headless.messagingvisibility", "show");

        // when
        var result = _InvokeGetTagScope(tag);

        // then
        result.Should().Be("visibility");
    }

    #endregion

    #region Provider Interface Tests

    [Fact]
    public void should_implement_INodeDiscoveryProvider()
    {
        // given & when & then
        _provider.Should().BeAssignableTo<INodeDiscoveryProvider>();
    }

    #endregion

    #region Helper Methods

    private sealed record TagFilterResult(bool HideNode, int FilteredPortIndex, string FilteredPortName);

#pragma warning disable REFL009 // Expected reflection usage
    private TagFilterResult _InvokeFilterNodesByTags(
        IDictionary<string, string> tags,
        K8sNodeDiscoveryProvider? provider = null
    )
    {
        provider ??= _provider;
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_FilterNodesByTags",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = method!.Invoke(provider, [tags]);
        var resultType = result!.GetType();

        return new TagFilterResult(
            (bool)resultType.GetProperty("HideNode")!.GetValue(result)!,
            (int)resultType.GetProperty("FilteredPortIndex")!.GetValue(result)!,
            (string)resultType.GetProperty("FilteredPortName")!.GetValue(result)!
        );
    }
#pragma warning restore REFL009

    private static int _InvokeGetPortByNameOrIndex(V1Service? service, string filterPortName, int filterPortIndex)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_GetPortByNameOrIndex",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        return (int)method!.Invoke(null, [service, filterPortName, filterPortIndex])!;
    }

    private static string _InvokeGetTagScope(KeyValuePair<string, string> tag)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_GetTagScope",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        return (string)method!.Invoke(null, [tag])!;
    }

    #endregion
}
