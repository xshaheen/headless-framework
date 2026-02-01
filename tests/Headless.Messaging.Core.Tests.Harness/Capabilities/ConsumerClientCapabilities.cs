// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>Flags indicating consumer client-specific capabilities for conditional test execution.</summary>
[PublicAPI]
public sealed class ConsumerClientCapabilities
{
    public static ConsumerClientCapabilities Default { get; } = new();

    /// <summary>Whether the consumer supports fetching topic metadata.</summary>
    public bool SupportsFetchTopics { get; init; } = true;

    /// <summary>Whether the consumer supports concurrent message processing.</summary>
    public bool SupportsConcurrentProcessing { get; init; } = true;

    /// <summary>Whether the consumer supports message rejection with requeue.</summary>
    public bool SupportsReject { get; init; } = true;

    /// <summary>Whether the consumer supports graceful shutdown.</summary>
    public bool SupportsGracefulShutdown { get; init; } = true;
}
