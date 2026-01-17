// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Internal;

/// <inheritdoc />
/// <summary>
/// A process thread abstract of message process.
/// </summary>
public interface IProcessingServer : IDisposable
{
    ValueTask StartAsync(CancellationToken stoppingToken);
}
