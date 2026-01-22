// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.NodeDiscovery;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard.K8s;

// ReSharper disable once InconsistentNaming
public class K8sNodeDiscoveryProvider(ILoggerFactory logger, K8sDiscoveryOptions options) : INodeDiscoveryProvider
{
    private const string _TagPrefix = "headless.messaging";
    private readonly ILogger<ConsulNodeDiscoveryProvider> _logger = logger.CreateLogger<ConsulNodeDiscoveryProvider>();

    public async Task<Node?> GetNode(string svcName, string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new Kubernetes(options.K8SClientConfig);
            var service = await client.CoreV1.ReadNamespacedServiceAsync(
                svcName,
                ns,
                cancellationToken: cancellationToken
            );

            return new Node
            {
                Id = service.Uid(),
                Name = service.Name(),
                Address = "http://" + service.Metadata.Name + "." + ns,
                Port = service.Spec.Ports?[0].Port ?? 0,
                Tags = string.Join(',', service.Labels()?.Select(x => x.Key + ":" + x.Value) ?? []),
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Get consul nodes raised an exception. Exception:{Message}", e.Message);
        }

        return null;
    }

    public async Task<IList<Node>> GetNodes(string? ns = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ns = options.K8SClientConfig.Namespace;

            if (ns == null)
            {
                return [];
            }

            var nodes = await ListServices(ns);

            MessagingCache.Global.AddOrUpdate("messaging.nodes.count", nodes.Count, TimeSpan.FromSeconds(60), true);

            return nodes;
        }
        catch (Exception ex)
        {
            MessagingCache.Global.AddOrUpdate("messaging.nodes.count", 0, TimeSpan.FromSeconds(20));

            _logger.LogError(ex, "Get k8s services raised an exception");

            return [];
        }
    }

    public async Task<List<string>> GetNamespaces(CancellationToken cancellationToken)
    {
        using var client = new Kubernetes(options.K8SClientConfig);

        try
        {
            var namespaces = await client.ListNamespaceAsync(cancellationToken: cancellationToken);
            return namespaces.Items.Select(x => x.Name()).ToList();
        }
#pragma warning disable ERP022
        catch (Exception)
        {
            if (string.IsNullOrEmpty(options.K8SClientConfig.Namespace))
            {
                return [];
            }

            return [options.K8SClientConfig.Namespace];
        }
#pragma warning restore ERP022
    }

    public async Task<IList<Node>> ListServices(string? ns = null)
    {
        using var client = new Kubernetes(options.K8SClientConfig);
        var services = await client.CoreV1.ListNamespacedServiceAsync(ns);

        var result = new List<Node>();
        foreach (var service in services.Items)
        {
            IDictionary<string, string> tags = service.Labels();

            var filterResult = _FilterNodesByTags(tags);

            if (filterResult.HideNode)
            {
                continue;
            }

            var port = _GetPortByNameOrIndex(service, filterResult.FilteredPortName, filterResult.FilteredPortIndex);

            result.Add(
                new Node
                {
                    Id = service.Uid(),
                    Name = service.Name(),
                    Address = "http://" + service.Metadata.Name + "." + ns,
                    Port = port,
                    Tags = string.Join(',', service.Labels()?.Select(x => x.Key + ":" + x.Value) ?? []),
                }
            );
        }

        return result;
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
        if (!string.IsNullOrEmpty(portName))
        {
            return 0;
        }

        var portByName = servicePorts.FirstOrDefault(p => p.Name == portName);
        if (portByName is null)
        {
            return 0;
        }

        return portByName.Port;
    }

    private sealed record TagFilterResult(bool HideNode, int FilteredPortIndex, string FilteredPortName);

    private TagFilterResult _FilterNodesByTags(IDictionary<string, string> tags)
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
            var isHeadlessMessagingTag = tag.Key.StartsWith(_TagPrefix, StringComparison.InvariantCultureIgnoreCase);

            if (!isHeadlessMessagingTag)
            {
                continue;
            }

            var messagingTagScope = _GetTagScope(tag);

            //check for hide Tag
            if (_IsNodeHidden(tag, messagingTagScope))
            {
                return new TagFilterResult(true, filteredPortIndex, filteredPortName);
            }

            isNodeHidden = false;

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
        var messagingTagScope = tag.Key.Replace(_TagPrefix, "", StringComparison.InvariantCultureIgnoreCase);

        if (messagingTagScope.StartsWith('.'))
        {
            messagingTagScope = messagingTagScope[1..];
        }

        return messagingTagScope;
    }
}
