// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>The complete persisted generation fence required for a lifecycle mutation.</summary>
[PublicAPI]
public sealed record InboxAttemptFence(
    Guid StorageId,
    MessageLane Lane,
    long Generation,
    Guid GenerationIncarnationId,
    Guid AttemptId,
    string? Owner,
    DateTimeOffset LockedUntil
);
