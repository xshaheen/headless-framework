// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// How eagerly a participant (a published message, an enqueued job) requires an active unit of work.
/// One shared knob for Messaging publishes and Jobs writes: whether the durable row must land inside
/// the active unit of work, may, or never does.
/// </summary>
/// <remarks>
/// Precedence for both Messaging and Jobs: per call &gt; per type/function &gt; host default. The guarantee
/// matrix: with a joinable compatible resource, every value enlists in the transaction; with no unit of work
/// (or a unit with no joinable resource), <see cref="WhenAvailable" /> writes autonomously while
/// <see cref="Required" /> throws; with an incompatible resource, everything except <see cref="Never" />
/// throws.
/// </remarks>
[PublicAPI]
public enum TransactionEnlistment
{
    /// <summary>
    /// Enlist when a joinable compatible unit of work is active; otherwise write autonomously (a durable
    /// standalone row with its own dispatch). The default.
    /// </summary>
    WhenAvailable = 0,

    /// <summary>
    /// An active unit of work with a joinable compatible resource is mandatory; publishing or scheduling
    /// without one throws before any effect.
    /// </summary>
    Required = 1,

    /// <summary>
    /// Never enlist: the write is autonomous even when a unit of work is active, and succeeds against an
    /// incompatible resource.
    /// </summary>
    Never = 2,
}
