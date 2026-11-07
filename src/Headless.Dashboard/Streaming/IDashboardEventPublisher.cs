// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Publishes a change a module made through this node to the dashboard's live streams at once, without waiting for the
/// next read of the module's <see cref="IDashboardChangeSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// A module calls <see cref="Publish"/> after its store commits a change the dashboard shows, such as a run that failed
/// or a message requeued from the dashboard. Streams on this node then show the change without waiting for the next
/// change read. Streams on other nodes still learn of it from their own read of the module's store, so a module that
/// implements <see cref="IDashboardChangeSource"/> loses nothing when it never publishes.
/// </para>
/// <para>
/// Publishing is in memory and never blocks. Changes to one object within 250 milliseconds reach the streams as one
/// event that carries the latest version, and a change whose <see cref="DashboardChange.Version"/> is not newer than
/// the last one sent for that object is dropped. Versions compare as integers when both parse as one, and ordinally
/// otherwise, so use a store sequence, or a fixed-width timestamp, as the version. Each stream delivers the change only
/// to a user who may view its module, and passes its projection through the module's
/// <see cref="IDashboardPayloadFormatter"/>.
/// </para>
/// <para>
/// The shell registers this service as a singleton; resolve it from the container. This interface grows by default
/// interface members; existing members never gain a parameter.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardEventPublisher
{
    /// <summary>Publishes <paramref name="change"/> to the live streams on this node.</summary>
    /// <param name="change">The changed object, its new version, and the projection a list row needs.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="change"/>, its target, or its version is <see langword="null"/>.
    /// </exception>
    void Publish(DashboardChange change);

    /// <summary>
    /// Reports that one node's data, as this node relays it, started or stopped being out of date, such as when the
    /// node stopped answering. Streams on this node that subscribe to a topic of the module for that node,
    /// <c>{module}.{kind}@{node}</c>, receive <c>node.stale</c>, and their views of that node alone show <c>Stale</c>.
    /// </summary>
    /// <remarks>
    /// The shell's own publisher implements it; the default, for a substitute publisher, sends nothing. The state is
    /// kept while a stream watches the node, so a stream that opens later is told at once. A report that repeats the
    /// current state sends nothing.
    /// </remarks>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="node">The node.</param>
    /// <param name="stale">Whether the node's data is out of date.</param>
    /// <exception cref="ArgumentException"><paramref name="moduleId"/> or <paramref name="node"/> is <see langword="null"/> or empty.</exception>
    void PublishNodeStale(string moduleId, string node, bool stale) { }

    /// <summary>
    /// Tells the views of one node to refetch, such as after the node's relayed changes were missed. Streams on this
    /// node that subscribe to a topic of the module for that node receive <c>node.reset</c>.
    /// </summary>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="node">The node.</param>
    /// <exception cref="ArgumentException"><paramref name="moduleId"/> or <paramref name="node"/> is <see langword="null"/> or empty.</exception>
    void PublishNodeReset(string moduleId, string node) { }
}
