// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Identifies the semantic lane selected by a messaging operation: broadcast (<see cref="Bus"/>)
/// or point-to-point (<see cref="Queue"/>).
/// </summary>
/// <remarks>
/// <para>
/// The values intentionally match the persisted <see cref="IntentType"/> representation during the
/// vocabulary migration. New runtime decisions use this type; compatibility boundaries convert explicitly.
/// </para>
/// <para>
/// This enum deliberately has no unknown sentinel. Unknown values are invalid compatibility data and must
/// be rejected rather than silently treated as <see cref="Bus"/>.
/// </para>
/// </remarks>
[PublicAPI]
#pragma warning disable CA1028 // The persisted compatibility contract uses SMALLINT.
public enum MessageLane : short
#pragma warning restore CA1028
{
    /// <summary>Broadcast lane (publish/subscribe).</summary>
    Bus = 0,

    /// <summary>Point-to-point lane (work queue).</summary>
    Queue = 1,
}
