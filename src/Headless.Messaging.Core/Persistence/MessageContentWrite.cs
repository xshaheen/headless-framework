// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Declares whether a state transition also rewrites the persisted message envelope.
/// </summary>
/// <remarks>
/// <para>
/// Storage keeps the invariant <c>persisted Content == Serialize(Origin)</c>. A transition that does not
/// touch <see cref="MediumMessage.Origin"/> therefore has nothing to write, and re-serializing the envelope
/// only to send back the bytes already stored costs a full serialization plus a large column write on the
/// hottest dispatch paths.
/// </para>
/// <para>
/// A caller that mutated <see cref="MediumMessage.Origin"/> before the transition (the failure paths, which
/// stamp exception information onto the envelope) MUST pass <see cref="Refresh"/> — otherwise the mutation
/// never reaches storage and the invariant breaks on the next pickup.
/// </para>
/// </remarks>
[PublicAPI]
public enum MessageContentWrite
{
    /// <summary>
    /// Leave the stored envelope untouched. Valid only when <see cref="MediumMessage.Origin"/> is byte-identical
    /// to what storage already holds.
    /// </summary>
    Preserve = 0,

    /// <summary>
    /// Re-serialize <see cref="MediumMessage.Origin"/>, write it to the row, and refresh
    /// <see cref="MediumMessage.Content"/> so the in-memory envelope keeps matching the persisted one.
    /// </summary>
    Refresh = 1,
}
