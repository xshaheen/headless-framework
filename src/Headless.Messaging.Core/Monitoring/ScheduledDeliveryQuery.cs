// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Payload-free filters for pending scheduled deliveries.</summary>
[PublicAPI]
public sealed class ScheduledDeliveryQuery
{
    public const int MaxStorageIds = 500;

    public string? MessageName { get; set; }

    public MessageLane? Lane { get; set; }

    public DateTimeOffset? DueFrom { get; set; }

    public DateTimeOffset? DueTo { get; set; }

    public IReadOnlyCollection<Guid>? StorageIds { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; } = 20;

    public void Validate()
    {
        if (StorageIds is { Count: > MaxStorageIds })
        {
            throw new InvalidOperationException($"Storage IDs filter cannot exceed {MaxStorageIds} items.");
        }
    }
}
