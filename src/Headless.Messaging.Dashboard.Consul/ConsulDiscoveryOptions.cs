// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.Consul;

/// <summary>
/// Configuration for Consul-based node discovery used by the Messaging Dashboard.
/// Controls how the current node registers itself with Consul and how the dashboard
/// discovers peer nodes to display in the multi-node federation view.
/// </summary>
public sealed class ConsulDiscoveryOptions
{
    /// <summary>Default Consul agent host name (<c>localhost</c>).</summary>
    public const string DefaultDiscoveryServerHost = "localhost";

    /// <summary>Default Consul HTTP API port (<c>8500</c>).</summary>
    public const int DefaultDiscoveryServerPort = 8500;

    /// <summary>Default host name used when registering this node (<c>localhost</c>).</summary>
    public const string DefaultCurrentNodeHostName = "localhost";

    /// <summary>Default port used when registering this node (<c>5000</c>).</summary>
    public const int DefaultCurrentNodePort = 5000;

    /// <summary>Default dashboard base path used for the Consul health-check URL (<c>/messaging</c>).</summary>
    public const string DefaultMatchPath = "/messaging";

    /// <summary>Default scheme for the health-check endpoint URL (<c>http</c>).</summary>
    public const string DefaultScheme = "http";

    /// <summary>Host name or IP of the Consul agent API endpoint. Default: <c>localhost</c>.</summary>
    public string DiscoveryServerHostName { get; set; } = DefaultDiscoveryServerHost;

    /// <summary>Port of the Consul agent API. Default: <c>8500</c>.</summary>
    public int DiscoveryServerPort { get; set; } = DefaultDiscoveryServerPort;

    /// <summary>Host name under which this node registers itself in Consul. Default: <c>localhost</c>.</summary>
    public string CurrentNodeHostName { get; set; } = DefaultCurrentNodeHostName;

    /// <summary>Port under which this node registers itself in Consul. Default: <c>5000</c>.</summary>
    public int CurrentNodePort { get; set; } = DefaultCurrentNodePort;

    /// <summary>Optional service ID. When <see langword="null"/> Consul uses <see cref="NodeName"/> as the ID.</summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Service name this node registers under in Consul, and the name the dashboard uses to recognize the current node.
    /// Set it: Consul rejects a registration without a service name (the failure is logged), and with no name the
    /// dashboard cannot tell a request for this node from a request for a peer.
    /// </summary>
    public string? NodeName { get; set; }

    /// <summary>Dashboard path segment appended to the health-check URL. Default: <c>/messaging</c>.</summary>
    public string MatchPath { get; set; } = DefaultMatchPath;

    /// <summary>URL scheme for the Consul health-check endpoint (<c>http</c> or <c>https</c>). Default: <c>http</c>.</summary>
    public string Scheme { get; set; } = DefaultScheme;

    /// <summary>Additional tags merged with the built-in Headless tags when registering with Consul.</summary>
    public string[]? CustomTags { get; set; }
}
