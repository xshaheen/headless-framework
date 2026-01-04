// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Queueing;

[PublicAPI]
public sealed class QueueEntryOptions
{
    public required string UniqueId { get; init; }

    public required string CorrelationId { get; init; }

    public TimeSpan? DeliveryDelay { get; init; }

    public IDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
