// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>A Messaging node discovery lists, other than this one.</summary>
/// <param name="Name">The node's name, which is also the first part of every id it serves.</param>
/// <param name="Addresses">The base address of each discovered instance of the node, in discovery order.</param>
internal sealed record MessagingPeer(string Name, IReadOnlyList<Uri> Addresses)
{
    /// <summary>
    /// Gets each discovered instance as discovery described it, in the order of <see cref="Addresses"/>, for the nodes
    /// view. Only instances with a callable address are listed.
    /// </summary>
    public IReadOnlyList<MessagingPeerInstance> Instances { get; init; } = [];
}

/// <summary>One discovered instance of a node, as discovery listed it.</summary>
/// <param name="Address">The host name or IP address discovery gave, which may carry a scheme.</param>
/// <param name="Port">The port discovery gave, or <c>0</c> when the address carries it.</param>
/// <param name="Tags">The discovery tags, split on commas, trimmed, with empty entries dropped.</param>
internal sealed record MessagingPeerInstance(string Address, int Port, IReadOnlyList<string> Tags);
