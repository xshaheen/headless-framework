// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Whether a Jobs write may run through the autonomous receiver (an injected <c>IJobScheduler</c> or manager) or
/// must run through the enlisted one (<c>unit.Jobs</c>), resolved per call &gt; per function &gt; host default,
/// strictest wins.
/// </summary>
/// <remarks>
/// Enlistment itself is decided by the receiver, never by this value: <c>unit.Jobs</c> always writes inside the
/// unit's transaction and refuses when the unit carries no joinable relational resource, and an injected
/// scheduler always writes autonomously. This knob only lets a function declare that the autonomous receiver is
/// not acceptable for it, so a schedule that would otherwise land outside a transaction fails before any effect.
/// </remarks>
[PublicAPI]
public enum TransactionEnlistment
{
    /// <summary>Either receiver is acceptable: enlisted through <c>unit.Jobs</c>, autonomous otherwise. The default.</summary>
    Optional = 0,

    /// <summary>
    /// Only the enlisted receiver is acceptable: scheduling through an injected scheduler or manager throws
    /// before any effect.
    /// </summary>
    Required = 1,
}
