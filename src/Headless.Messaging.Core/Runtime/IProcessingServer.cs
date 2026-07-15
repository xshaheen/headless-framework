// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Runtime;

/// <summary>
/// A background processing unit that runs for the lifetime of the messaging host — for example the
/// consumer register, the retry dispatcher, or a node-discovery registrar. Providers and dashboards
/// implement this to plug their own long-running work into the messaging bootstrap sequence.
/// </summary>
[PublicAPI]
public interface IProcessingServer : IAsyncDisposable
{
    /// <summary>Starts the processing loop and returns once startup has completed.</summary>
    /// <param name="stoppingToken">Signals that the host is shutting down and the loop should stop.</param>
    ValueTask StartAsync(CancellationToken stoppingToken);
}
