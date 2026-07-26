// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>
/// A safe, read-only projection of a row whose persisted delivery lane is unknown.
/// </summary>
/// <remarks>
/// This projection deliberately excludes serialized content. Reading unknown-lane diagnostics never
/// deserializes the stored envelope or attempts to repair the row.
/// </remarks>
[PublicAPI]
public sealed class UnknownLaneMessageView
{
    /// <summary>Gets or sets the internal storage row identifier.</summary>
    public required Guid StorageId { get; set; }

    /// <summary>Gets or sets whether the row is in published or received storage.</summary>
    public MessageType MessageType { get; set; }

    /// <summary>Gets or sets the raw numeric value stored in the legacy <c>IntentType</c> column.</summary>
    public short RawLane { get; set; }

    /// <summary>Gets or sets the stored message name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the current stored status.</summary>
    public StatusName StatusName { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which the row was added.</summary>
    public DateTimeOffset Added { get; set; }

    /// <summary>Gets or sets when a retry becomes eligible, if one is scheduled.</summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>Gets or sets the active lease deadline, if present.</summary>
    public DateTimeOffset? LockedUntil { get; set; }
}
