// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Holds this node in membership during host stop until the work it owns has drained. Supplied by consumers and
/// awaited by the coordination heartbeat before the node leaves.
/// </summary>
/// <remarks>
/// On host stop the heartbeat awaits every registered participant while it keeps beating, and only then stops
/// beating and leaves. A node that left, or that stopped beating long enough to self-fence, is dead to its peers:
/// they reclaim the rows it still owns while its in-flight work is settling them. Waiting here keeps the node a live
/// owner until that work has written its outcome. Register each implementation as a singleton
/// <see cref="INodeDrainParticipant"/>; the heartbeat resolves all of them. Under the default sequential host stop,
/// services registered after coordination stop first, so their participant has usually completed by the time the
/// heartbeat asks; the wait matters when <c>HostOptions.ServicesStopConcurrently</c> is set.
/// </remarks>
[PublicAPI]
public interface INodeDrainParticipant
{
    /// <summary>Waits until the work this participant owns on this node has drained and written its outcome.</summary>
    /// <param name="cancellationToken">
    /// The host stop token. When it fires, the heartbeat stops waiting and leaves anyway, so an implementation should
    /// return or throw <see cref="OperationCanceledException"/> promptly rather than outlast it.
    /// </param>
    /// <returns>A task that completes once the participant has drained.</returns>
    Task WaitUntilDrainedAsync(CancellationToken cancellationToken);
}
