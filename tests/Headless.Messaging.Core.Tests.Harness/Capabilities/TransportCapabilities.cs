// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>Flags indicating transport-specific capabilities for conditional test execution.</summary>
[PublicAPI]
public sealed class TransportCapabilities
{
    public static TransportCapabilities Default { get; } = new();

    public bool SupportsOrdering { get; init; }

    public bool SupportsDeadLetter { get; init; }

    public bool SupportsPriority { get; init; }

    public bool SupportsDelayedDelivery { get; init; }

    public bool SupportsBatchSend { get; init; } = true;

    public bool SupportsHeaders { get; init; } = true;
}
