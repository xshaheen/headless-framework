// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A module capability: learns when this node's browser streams start and stop watching one of the module's node
/// topics, <c>{module}.{kind}@{node}</c>, so the module reads or relays a node's changes only while someone watches
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The shell calls <see cref="OnNodeTopicWatched"/> when the first open stream on this node subscribes to a node topic
/// of the module, and <see cref="OnNodeTopicUnwatched"/> when the last stream subscribed to it closes. Calls for one
/// node are made one at a time, in order, and each watched topic is unwatched exactly once. A topic of a node the
/// module does not know is still reported; the module ignores it.
/// </para>
/// <para>
/// Both calls run while the stream opens or closes, so they must return at once: start or stop background work, never
/// wait for it. A call that throws is logged and does not affect the stream.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardNodeTopicObserver
{
    /// <summary>Called when the first stream on this node subscribes to <paramref name="topic"/>.</summary>
    /// <param name="topic">The node topic now watched.</param>
    void OnNodeTopicWatched(DashboardNodeTopic topic);

    /// <summary>Called when the last stream on this node subscribed to <paramref name="topic"/> closes.</summary>
    /// <param name="topic">The node topic no longer watched.</param>
    void OnNodeTopicUnwatched(DashboardNodeTopic topic);
}

/// <summary>One node topic, <c>{module}.{kind}@{node}</c>, that this node's streams watch.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardNodeTopic
{
    /// <summary>Gets the module's id.</summary>
    public required string Module { get; init; }

    /// <summary>Gets the object kind, such as <c>received</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the node, such as <c>node-b</c>.</summary>
    public required string Node { get; init; }

    /// <summary>Gets the topic as streams name it: <c>{module}.{kind}@{node}</c>.</summary>
    public string Topic => Module + "." + Kind + "@" + Node;
}
