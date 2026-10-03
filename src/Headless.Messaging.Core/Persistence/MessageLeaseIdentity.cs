// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Identifies one store-authoritative retry lease generation.
/// </summary>
/// <param name="StorageId">The durable message row identifier.</param>
/// <param name="Owner">The exact owner returned by the claim operation.</param>
/// <param name="LockedUntil">The exact lease deadline returned by the claim operation.</param>
/// <param name="Lane">The persisted Bus or Queue lane returned by the claim operation.</param>
/// <param name="InboxAttemptFence">The complete inbox attempt fence when the lease belongs to an inbox row.</param>
[PublicAPI]
public readonly record struct MessageLeaseIdentity(
    Guid StorageId,
    string? Owner,
    DateTimeOffset LockedUntil,
    MessageLane Lane,
    InboxAttemptFence? InboxAttemptFence = null
);
