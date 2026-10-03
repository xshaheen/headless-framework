// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>The short-circuit outcome a receive middleware declared for a delivery.</summary>
internal enum ReceiveOutcome
{
    /// <summary>No outcome declared yet; the pipeline treats a return without one as a reject.</summary>
    None,

    /// <summary>Commit the delivery without consumer invocation, storage, or an exhausted callback.</summary>
    Skip,

    /// <summary>Commit the delivery as poison-on-arrival with the received envelope.</summary>
    Reject,
}
