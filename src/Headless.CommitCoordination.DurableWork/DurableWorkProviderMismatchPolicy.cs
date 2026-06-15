// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.DurableWork;

/// <summary>
/// Controls behavior when durable work is enlisted without a matching relational commit capability.
/// </summary>
[PublicAPI]
public enum DurableWorkProviderMismatchPolicy
{
    /// <summary>
    /// Throw immediately because the durable row cannot be written inside the physical transaction.
    /// </summary>
    Throw,

    /// <summary>
    /// Allow the caller to continue after the derived buffer records a non-transactional fallback.
    /// </summary>
    Warn,
}
