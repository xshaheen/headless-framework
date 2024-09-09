// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Queueing;

[PublicAPI]
public sealed class QueueEntryOptions
{
    public required string UniqueId { get; set; }

    public required string CorrelationId { get; set; }

    public TimeSpan? DeliveryDelay { get; set; }

    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
}
