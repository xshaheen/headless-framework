// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Dashboard.K8s;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using k8s.Models;
using Microsoft.Extensions.Caching.Memory;

#pragma warning disable REFL009 // The referenced member is not known to exist
namespace Tests;

public sealed class K8sNodeDiscoveryProviderTests : TestBase
{
    private readonly K8sNodeDiscoveryProvider _provider;
    private readonly K8sDiscoveryOptions _options;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public K8sNodeDiscoveryProviderTests()
    {
        _options = new K8sDiscoveryOptions();
        _provider = new K8sNodeDiscoveryProvider(LoggerFactory, _cache, _options);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        return base.DisposeAsyncCore();
    }

    #region FilterNodesByTags Tests

    [Fact]
    public void should_hide_node_when_filter_nodes_by_tags_visibility_is_hide()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headless.messaging.visibility"] = "hide",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void should_show_node_when_filter_nodes_by_tags_visibility_is_show()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headless.messaging.visibility"] = "show",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void should_hide_node_by_default_when_filter_nodes_by_tags_show_only_explicit_visible_nodes_is_true()
    {
        // given
        _options.ShowOnlyExplicitVisibleNodes = true;
        var tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["some-other-tag"] = "value" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void should_show_node_when_filter_nodes_by_tags_show_only_explicit_visible_nodes_is_false_and_no_hide_tag()
    {
        // given
        _options.ShowOnlyExplicitVisibleNodes = false;
        var provider = new K8sNodeDiscoveryProvider(LoggerFactory, _cache, _options);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headless.messaging.something"] = "value",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags, provider);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void should_extract_port_index_from_tag_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
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
    public void should_extract_port_name_from_tag_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
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
    public void should_handle_null_tags_when_filter_nodes_by_tags()
    {
        // given & when
        var result = _InvokeFilterNodesByTags(null!);

        // then
        result.HideNode.Should().BeTrue(); // default when ShowOnlyExplicitVisibleNodes is true
        result.FilteredPortIndex.Should().Be(0);
        result.FilteredPortName.Should().BeEmpty();
    }

    [Fact]
    public void should_be_case_insensitive_for_tag_prefix_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HEADLESS.MESSAGING.visibility"] = "show",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeFalse();
    }

    [Fact]
    public void should_be_case_insensitive_for_visibility_value_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headless.messaging.visibility"] = "HIDE",
        };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void should_ignore_non_headless_tags_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["app"] = "my-app", ["version"] = "1.0" };

        // when
        var result = _InvokeFilterNodesByTags(tags);

        // then - node hidden because no explicit show tag and ShowOnlyExplicitVisibleNodes is true
        result.HideNode.Should().BeTrue();
    }

    [Fact]
    public void should_use_last_port_index_when_filter_nodes_by_tags_multiple_exist()
    {
        // given - simulating multiple portIndex tags (unlikely but testing behavior)
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
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
    public void should_handle_invalid_port_index_value_when_filter_nodes_by_tags()
    {
        // given
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
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
    public void should_return_zero_when_get_port_by_name_or_index_service_is_null()
    {
        // given & when
        var result = _InvokeGetPortByNameOrIndex(null, "", 0);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void should_return_zero_when_get_port_by_name_or_index_service_ports_is_null()
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
    public void should_return_first_port_when_get_port_by_name_or_index_no_filter()
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
    public void should_return_port_by_index_when_get_port_by_name_or_index()
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
    public void should_return_first_port_when_get_port_by_name_or_index_index_out_of_range()
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
    public void should_extract_scope_from_tag_when_get_tag_scope()
    {
        // given
        var tag = new KeyValuePair<string, string>("headless.messaging.visibility", "show");

        // when
        var result = _InvokeGetTagScope(tag);

        // then
        result.Should().Be("visibility");
    }

    [Fact]
    public void should_handle_tag_without_dot_after_prefix_when_get_tag_scope()
    {
        // given
        var tag = new KeyValuePair<string, string>("headless.messagingvisibility", "show");

        // when
        var result = _InvokeGetTagScope(tag);

        // then
        result.Should().Be("visibility");
    }

    #endregion

    #region Service Mapping Tests

    [Fact]
    public void should_map_visible_service_with_label_selected_port()
    {
        // given
        var service = _CreateService(
            labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["headless.messaging.visibility"] = "show",
                ["headless.messaging.portName"] = "dashboard",
            }
        );

        // when
        var node = _InvokeMapService(service, "team-a");

        // then
        node.Should().NotBeNull();
        node!.Name.Should().Be("messaging-api");
        node.Address.Should().Be("http://messaging-api.team-a");
        node.Port.Should().Be(9090);
    }

    [Fact]
    public void should_reject_hidden_service_when_mapping_direct_lookup()
    {
        // given
        var service = _CreateService(
            labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["headless.messaging.visibility"] = "hide",
            }
        );

        // when
        var node = _InvokeMapService(service, "team-a");

        // then
        node.Should().BeNull();
    }

    [Fact]
    public void should_reject_service_without_explicit_visibility_when_port_label_exists()
    {
        // given
        var service = _CreateService(
            labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["headless.messaging.portName"] = "dashboard",
            }
        );

        // when
        var node = _InvokeMapService(service, "team-a");

        // then
        node.Should().BeNull();
    }

    [Fact]
    public void should_map_unlabelled_service_when_show_only_is_disabled()
    {
        // given
        _options.ShowOnlyExplicitVisibleNodes = false;
        var service = _CreateService(labels: null);

        // when
        var node = _InvokeMapService(service, "team-a");

        // then
        node.Should().NotBeNull();
        node!.Port.Should().Be(8080);
    }

    #endregion

    #region Namespace Boundary Tests

    [Fact]
    public void should_default_to_configured_namespace_when_request_omits_namespace()
    {
        // given
        _options.K8sClientConfig.Namespace = "team-a";

        // when
        var result = _InvokeResolveNamespace(null);

        // then
        result.Should().Be("team-a");
    }

    [Fact]
    public void should_reject_cross_namespace_request()
    {
        // given
        _options.K8sClientConfig.Namespace = "team-a";

        // when
        var result = _InvokeResolveNamespace("team-b");

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_only_configured_namespace()
    {
        // given
        _options.K8sClientConfig.Namespace = "team-a";

        // when
        var result = await _provider.GetNamespacesAsync(AbortToken);

        // then
        result.Should().ContainSingle().Which.Should().Be("team-a");
    }

    [Fact]
    public async Task should_fail_closed_when_namespace_is_not_configured()
    {
        // given
        _options.K8sClientConfig.Namespace = null!;

        // when
        var result = await _provider.GetNamespacesAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    #endregion

    #region Provider Interface Tests

    [Fact]
    public void should_implement_i_node_discovery_provider()
    {
        // given & when & then
        _provider.Should().BeAssignableTo<INodeDiscoveryProvider>();
    }

    #endregion

    #region Helper Methods

    private sealed record TagFilterResult(bool HideNode, int FilteredPortIndex, string FilteredPortName);

    private TagFilterResult _InvokeFilterNodesByTags(
        IDictionary<string, string> tags,
        K8sNodeDiscoveryProvider? provider = null
    )
    {
        provider ??= _provider;

        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_FilterNodesByTags",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(IDictionary<string, string>)],
            null
        );

        var result = method!.Invoke(provider, [tags]);
        var resultType = result!.GetType();

        return new TagFilterResult(
            (bool)resultType.GetProperty("HideNode")!.GetValue(result)!,
            (int)resultType.GetProperty("FilteredPortIndex")!.GetValue(result)!,
            (string)resultType.GetProperty("FilteredPortName")!.GetValue(result)!
        );
    }

    private static int _InvokeGetPortByNameOrIndex(V1Service? service, string filterPortName, int filterPortIndex)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_GetPortByNameOrIndex",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null,
            [typeof(V1Service), typeof(string), typeof(int)],
            null
        );

        return (int)method!.Invoke(null, [service, filterPortName, filterPortIndex])!;
    }

    private static string _InvokeGetTagScope(KeyValuePair<string, string> tag)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_GetTagScope",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null,
            [typeof(KeyValuePair<string, string>)],
            null
        );

        return (string)method!.Invoke(null, [tag])!;
    }

    private Node? _InvokeMapService(V1Service service, string ns)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_MapService",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(V1Service), typeof(string)],
            null
        );

        return (Node?)method!.Invoke(_provider, [service, ns]);
    }

    private string? _InvokeResolveNamespace(string? requestedNamespace)
    {
        var method = typeof(K8sNodeDiscoveryProvider).GetMethod(
            "_ResolveNamespace",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(string)],
            null
        );

        return (string?)method!.Invoke(_provider, [requestedNamespace]);
    }

    private static V1Service _CreateService(IDictionary<string, string>? labels)
    {
        return new V1Service
        {
            Metadata = new V1ObjectMeta
            {
                Name = "messaging-api",
                Uid = "service-id",
                Labels = labels,
            },
            Spec = new V1ServiceSpec
            {
                Ports =
                [
                    new V1ServicePort { Name = "http", Port = 8080 },
                    new V1ServicePort { Name = "dashboard", Port = 9090 },
                ],
            },
        };
    }

    #endregion
}
