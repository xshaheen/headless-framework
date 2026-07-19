// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.NodeDiscovery;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard.K8s;

/// <summary>
/// Kubernetes-backed implementation of <see cref="INodeDiscoveryProvider"/>.
/// Queries the Kubernetes API for services in the configured namespace and maps them to
/// dashboard nodes, applying visibility and port-selection rules derived from
/// <c>headless.messaging.*</c> labels on each service.
/// </summary>
public class K8sNodeDiscoveryProvider(ILoggerFactory logger, IMemoryCache cache, K8sDiscoveryOptions options)
    : INodeDiscoveryProvider
{
    private const string _TagPrefix = "headless.messaging";
    private readonly ILogger<K8sNodeDiscoveryProvider> _logger = logger.CreateLogger<K8sNodeDiscoveryProvider>();

    public async Task<Node?> GetNodeAsync(
        string nodeName,
        string? ns = null,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedNamespace = _ResolveNamespace(ns);
        if (resolvedNamespace == null)
        {
            return null;
        }

        try
        {
            using var client = new Kubernetes(options.K8sClientConfig);
            var service = await client
                .CoreV1.ReadNamespacedServiceAsync(nodeName, resolvedNamespace, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return _MapService(service, resolvedNamespace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogGetK8sNodeFailed(e, e.Message);
        }

        return null;
    }

    public async Task<IList<Node>> GetNodesAsync(string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedNamespace = _ResolveNamespace(ns);
            if (resolvedNamespace == null)
            {
                return [];
            }

            var nodes = await _ListServices(resolvedNamespace, cancellationToken).ConfigureAwait(false);

            cache.Set("messaging.nodes.count", nodes.Count, TimeSpan.FromSeconds(60));

            return nodes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cache.Set("messaging.nodes.count", 0, TimeSpan.FromSeconds(20));

            _logger.LogGetK8sServicesFailed(ex);

            return [];
        }
    }

    public Task<List<string>> GetNamespacesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuredNamespace = _ResolveNamespace(null);
        List<string> namespaces = configuredNamespace == null ? [] : [configuredNamespace];

        return Task.FromResult(namespaces);
    }

    public Task<IList<Node>> ListServicesAsync(string? ns = null, CancellationToken cancellationToken = default)
    {
        return _ListServices(ns, cancellationToken);
    }

    private async Task<IList<Node>> _ListServices(string? ns, CancellationToken cancellationToken)
    {
        var resolvedNamespace = _ResolveNamespace(ns);
        if (resolvedNamespace == null)
        {
            return [];
        }

        using var client = new Kubernetes(options.K8sClientConfig);
        var services = await client
            .CoreV1.ListNamespacedServiceAsync(resolvedNamespace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = new List<Node>();
        foreach (var service in services.Items)
        {
            var node = _MapService(service, resolvedNamespace);
            if (node == null)
            {
                continue;
            }

            result.Add(node);
        }

        return result;
    }

    private string? _ResolveNamespace(string? requestedNamespace)
    {
        var configuredNamespace = options.K8sClientConfig.Namespace;
        if (string.IsNullOrWhiteSpace(configuredNamespace))
        {
            return null;
        }

        if (
            requestedNamespace != null
            && !string.Equals(requestedNamespace, configuredNamespace, StringComparison.Ordinal)
        )
        {
            return null;
        }

        return configuredNamespace;
    }

    private Node? _MapService(V1Service service, string ns)
    {
        var filterResult = _FilterNodesByTags(service.Labels());
        if (filterResult.HideNode)
        {
            return null;
        }

        var port = _GetPortByNameOrIndex(service, filterResult.FilteredPortName, filterResult.FilteredPortIndex);

        return new Node
        {
            Id = service.Uid(),
            Name = service.Name(),
            Address = "http://" + service.Metadata.Name + "." + ns,
            Port = port,
            Tags = string.Join(',', service.Labels()?.Select(x => x.Key + ":" + x.Value) ?? []),
        };
    }

    /// <summary>
    /// Given the filters (filterPortName and filterPortIndex) this method will try to find the port
    /// filterPortName is checked first and if no port is found by that name filterPortIndex is checked
    /// Returns 0 if service is null or no port specified in the service
    /// Returns the portNumber of the matched port if something is found
    /// </summary>
    /// <param name="service"></param>
    /// <param name="filterPortName"></param>
    /// <param name="filterPortIndex"></param>
    /// <returns></returns>
    private static int _GetPortByNameOrIndex(V1Service? service, string filterPortName, int filterPortIndex)
    {
        if (service is null)
        {
            return 0;
        }

        if (service.Spec.Ports is null)
        {
            return 0;
        }

        var result = _GetPortByName(service.Spec.Ports, filterPortName);
        if (result > 0)
        {
            return result;
        }

        result = _GetPortByIndex(service.Spec.Ports, filterPortIndex);
        if (result > 0)
        {
            return result;
        }

        return service.Spec.Ports[0]?.Port ?? 0;
    }

    /// <summary>
    /// This method will try to find a port with the specified Index
    /// Will Return 0 if index is not found
    /// Returns: port number or 0 if not found
    /// </summary>
    /// <param name="servicePorts"></param>
    /// <param name="filterIndex"></param>
    /// <returns></returns>
    private static int _GetPortByIndex(IList<V1ServicePort> servicePorts, int filterIndex)
    {
        var portByIndex = servicePorts.ElementAtOrDefault(filterIndex);
        if (portByIndex is null)
        {
            return 0;
        }

        return portByIndex.Port;
    }

    /// <summary>
    /// This method will try to find a port with the specified name
    /// Will Return 0 if none found
    /// Returns: port number or 0 if not found
    /// </summary>
    private static int _GetPortByName(IList<V1ServicePort> servicePorts, string portName)
    {
        if (string.IsNullOrEmpty(portName))
        {
            return 0;
        }

        var portByName = servicePorts.FirstOrDefault(p => string.Equals(p.Name, portName, StringComparison.Ordinal));
        if (portByName is null)
        {
            return 0;
        }

        return portByName.Port;
    }

    private sealed record TagFilterResult(bool HideNode, int FilteredPortIndex, string FilteredPortName);

    private TagFilterResult _FilterNodesByTags(IDictionary<string, string>? tags)
    {
        var isNodeHidden = options.ShowOnlyExplicitVisibleNodes;
        var filteredPortIndex = 0; //this the default port index
        var filteredPortName = string.Empty; //this the default port index

        if (tags == null)
        {
            return new TagFilterResult(isNodeHidden, filteredPortIndex, filteredPortName);
        }

        foreach (var tag in tags)
        {
            //look out for headless.messaging tags
            //based on value will do conditions
            var isHeadlessMessagingTag = tag.Key.StartsWith(_TagPrefix, StringComparison.OrdinalIgnoreCase);

            if (!isHeadlessMessagingTag)
            {
                continue;
            }

            var messagingTagScope = _GetTagScope(tag);

            //check for hide Tag
            if (_IsNodeHidden(tag, messagingTagScope))
            {
                return new TagFilterResult(HideNode: true, filteredPortIndex, filteredPortName);
            }

            if (
                messagingTagScope.Equals("visibility", StringComparison.OrdinalIgnoreCase)
                && tag.Value.Equals("show", StringComparison.OrdinalIgnoreCase)
            )
            {
                isNodeHidden = false;
            }

            //check for portIndex-X tag.
            //If multiple tags with portIndex are found only the last has power
            var hasNewPort = _CheckFilterPortIndex(tag, messagingTagScope);
            if (hasNewPort.HasValue)
            {
                filteredPortIndex = hasNewPort.Value;
            }

            //check for portName-X tag.
            //If multiple tags with portName are found only the last has power
            if (messagingTagScope.Equals("portName", StringComparison.OrdinalIgnoreCase))
            {
                filteredPortName = tag.Value;
            }
        }

        return new TagFilterResult(isNodeHidden, filteredPortIndex, filteredPortName);
    }

    private static int? _CheckFilterPortIndex(KeyValuePair<string, string> tag, string messagingTagScope)
    {
        if (!messagingTagScope.Equals("portIndex", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hasPort = int.TryParse(tag.Value, CultureInfo.InvariantCulture, out var filterPort);

        if (!hasPort)
        {
            return null;
        }

        return filterPort;
    }

    private bool _IsNodeHidden(KeyValuePair<string, string> tag, string messagingTagScope)
    {
        if (!messagingTagScope.Equals("visibility", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        //We will not show the node if the tag value is "headless.messaging.visibility:hide"
        if (tag.Value.Equals("hide", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        //We will not show the node if the K8s Dashboard option is
        //ShowOnlyExplicitVisibleNodes=True
        //and the tag value is NOT "headless.messaging.visibility:show"
        if (!options.ShowOnlyExplicitVisibleNodes)
        {
            return false;
        }

        return !tag.Value.Equals("show", StringComparison.OrdinalIgnoreCase);
    }

    private static string _GetTagScope(KeyValuePair<string, string> tag)
    {
        var messagingTagScope = tag.Key.Replace(_TagPrefix, "", StringComparison.OrdinalIgnoreCase);

        if (messagingTagScope.StartsWith('.'))
        {
            messagingTagScope = messagingTagScope[1..];
        }

        return messagingTagScope;
    }
}

internal static partial class K8sNodeDiscoveryProviderLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GetK8sNodeFailed",
        Level = LogLevel.Error,
        Message = "Get k8s node raised an exception. Exception:{Message}"
    )]
    public static partial void LogGetK8sNodeFailed(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 2,
        EventName = "GetK8sServicesFailed",
        Level = LogLevel.Error,
        Message = "Get k8s services raised an exception"
    )]
    public static partial void LogGetK8sServicesFailed(this ILogger logger, Exception exception);
}
