// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <inheritdoc />
/// <summary>
/// A process thread abstract of message process.
/// </summary>
public interface IProcessingServer : IAsyncDisposable
{
    ValueTask StartAsync(CancellationToken stoppingToken);
}
