// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.NodeDiscovery;

/// <summary>
/// Represents a discovered messaging node in the dashboard federation.
/// Each node exposes its own dashboard API and can be selected in the UI to proxy requests to its data.
/// </summary>
public class Node
{
    /// <summary>Unique identifier assigned by the discovery backend (service ID in Consul, UID in Kubernetes).</summary>
    public required string Id { get; set; }

    /// <summary>Human-readable service name used to identify the node in the dashboard UI.</summary>
    public required string Name { get; set; }

    /// <summary>Host name or IP address where the node's dashboard API is reachable.</summary>
    public required string Address { get; set; }

    /// <summary>TCP port where the node's dashboard API is listening.</summary>
    public required int Port { get; set; }

    /// <summary>Comma-separated discovery tags associated with the node (e.g., labels from Kubernetes or tags from Consul).</summary>
    public required string Tags { get; set; }

    /// <summary>Round-trip latency measured by the last health ping, or <see langword="null"/> if not yet measured.</summary>
    public string? Latency { get; set; }
}
