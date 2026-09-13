// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Identifies a message accepted by the transport or captured in durable storage.</summary>
/// <param name="MessageId">
/// The resolved wire message identity, including middleware overrides. Null when middleware suppresses
/// publication without executing the terminal publish operation.
/// </param>
/// <param name="StorageId">
/// The durable row identity. Null for direct delivery or suppressed publication.
/// A coordinated write remains subject to the enclosing transaction's commit or rollback.
/// </param>
/// <remarks>A receipt does not imply consumer completion or successful transaction commit.</remarks>
[PublicAPI]
public readonly record struct PublishReceipt(string? MessageId, Guid? StorageId);
