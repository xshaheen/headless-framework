// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>The immutable logical identity of one retained inbox generation.</summary>
[PublicAPI]
public sealed record InboxKey(
    string? TenantId,
    string MessageId,
    MessageLane Lane,
    string ContractIdentity,
    string ContractVersion,
    string ConsumerIdentity,
    long Generation
);
