// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>The outcome of revoking an accepted scheduled message.</summary>
[PublicAPI]
public enum MessageRevocationResult
{
    /// <summary>No published row exists for this handle in the configured storage version.</summary>
    NotFound = 0,

    /// <summary>The row was deleted before any dispatch reservation and will not reach a transport.</summary>
    Revoked = 1,

    /// <summary>The row cannot be revoked because dispatch or retry state prevents it.</summary>
    /// <remarks>This is not proof of delivery. Unscheduled rows with initial-dispatch grace are also ineligible.</remarks>
#pragma warning disable CA1700 // Reserved describes a dispatch attempt, not a placeholder enum value.
    AttemptReserved = 2,
#pragma warning restore CA1700
}
