// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Safe operator projection of one pending scheduled delivery.</summary>
[PublicAPI]
public sealed record ScheduledDeliveryView(
    Guid StorageId,
    string MessageId,
    string MessageName,
    MessageLane Lane,
    DateTimeOffset ExpectedDueAt,
    string Status,
    bool IsLeased,
    string? Owner,
    DateTimeOffset? LockedUntil,
    int InlineAttempts
);
