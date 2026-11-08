// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>The names both ends of a forwarded call agree on.</summary>
internal static class ForwardingProtocol
{
    /// <summary>The authentication scheme and the authorization policy that admit a peer's call.</summary>
    public const string Scheme = "HeadlessDashboardForwarding";

    /// <summary>The named <see cref="HttpClient"/> every forwarded call goes through.</summary>
    public const string ClientName = "Headless.Messaging.Dashboard.Forwarding";

    /// <summary>Marks a forwarded call and names the serving node; a peer serves such a call itself.</summary>
    public const string HopHeader = "X-Headless-Forwarded";

    public const string KeyHeader = "X-Headless-Forwarding-Key";

    /// <summary>The operator's name, escaped with <see cref="Uri.EscapeDataString(string)"/>.</summary>
    public const string ActorHeader = "X-Headless-Forwarded-Actor";

    /// <summary>The serving node's decision for the operator: <c>view</c> or <c>operate</c>.</summary>
    public const string AccessHeader = "X-Headless-Forwarded-Access";

    /// <summary>Why a peer refused a forwarded call, so the serving node names the node's state.</summary>
    public const string RefusalHeader = "X-Headless-Forwarding-Refusal";

    /// <summary>The state of the node a relayed read could not reach.</summary>
    public const string NodeStateHeader = "X-Headless-Node-State";

    public const string AccessView = "view";
    public const string AccessOperate = "operate";

    public const string AdmittedClaim = "headless:forwarding:admitted";
    public const string AccessClaim = "headless:forwarding:access";
    public const string FromClaim = "headless:forwarding:from";

    public const string RefusalKey = "key-rejected";
    public const string RefusalPolicy = "policy-denied";
    public const string RefusalAccess = "access-denied";
}

/// <summary>The state of a peer the last forwarded call to it found.</summary>
internal enum PeerState
{
    Answering = 0,
    NotAnswering = 1,
    KeyRejected = 2,
    PolicyDenied = 3,
    ViewOnly = 4,
    InsecurePeer = 5,
}

internal static class PeerStates
{
    public static string ToWire(this PeerState state) =>
        state switch
        {
            PeerState.Answering => "answering",
            PeerState.NotAnswering => "not-answering",
            PeerState.KeyRejected => "key-rejected",
            PeerState.PolicyDenied => "policy-denied",
            PeerState.ViewOnly => "view-only",
            PeerState.InsecurePeer => "insecure-peer",
            _ => "not-answering",
        };

    /// <summary>The sentence an operator reads for a peer in this state.</summary>
    public static string Describe(this PeerState state, string node) =>
        state switch
        {
            PeerState.Answering => $"Node {node} is answering",
            PeerState.KeyRejected => $"Node {node} rejected this node's forwarding key",
            PeerState.PolicyDenied => $"Node {node} denied this node by its forwarding policy",
            PeerState.ViewOnly => ViewOnly(node),
            PeerState.InsecurePeer =>
                $"Node {node} is an insecure peer: its address is not https, and AllowInsecurePeers is off",
            _ => $"Node {node} is not answering",
        };

    public static string ViewOnly(string node) => $"Node {node} accepts view-only forwarded calls";
}
