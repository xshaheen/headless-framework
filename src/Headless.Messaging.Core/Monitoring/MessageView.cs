// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

[PublicAPI]
public class MessageView
{
    public required long StorageId { get; set; }

    public required string MessageId { get; set; }

    public required string Version { get; set; }

    public string? Group { get; set; }

    public required string Name { get; set; }

    public string? Content { get; set; }

    public DateTime Added { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int Retries { get; set; }

    public required string StatusName { get; set; }

    /// <summary>UTC timestamp at which the retry processor should re-dispatch this message; <see langword="null"/> when the row is terminal or has no pending retry.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>UTC timestamp until which the row is leased by an active dispatch attempt; <see langword="null"/> when no lease is active.</summary>
    public DateTime? LockedUntil { get; set; }
}
