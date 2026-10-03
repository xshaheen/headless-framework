// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>Describes the strongest inbox guarantee a storage provider can enforce.</summary>
[PublicAPI]
public enum MessagingInboxCapabilityTier
{
    /// <summary>State and duplicate suppression are process-local and do not survive restart.</summary>
    ProcessLocal = 0,

    /// <summary>Inbox state is durable, but its outcome cannot commit atomically with application state.</summary>
    DurableDedupeOnly = 1,

    /// <summary>
    /// Inbox outcome, compatible enlisted application state, and captured outgoing work can commit atomically.
    /// </summary>
    Transactional = 2,
}
