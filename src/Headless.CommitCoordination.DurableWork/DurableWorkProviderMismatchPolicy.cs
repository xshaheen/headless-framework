// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.DurableWork;

/// <summary>
/// Controls the behavior of <c>DurableWorkBuffer{TRow}.EnlistAsync</c> when no
/// <see cref="IRelationalCommitContext" /> capability is attached to the current commit coordinator.
/// </summary>
[PublicAPI]
public enum DurableWorkProviderMismatchPolicy
{
    /// <summary>
    /// Throw an <see cref="InvalidOperationException" /> immediately because the durable row cannot be written
    /// inside a physical transaction. This is the default (fail-closed) behavior.
    /// </summary>
    Throw,

    /// <summary>
    /// Log a warning and delegate to <c>DurableWorkBuffer{TRow}.EnlistWithoutRelationalContextAsync</c>. Only
    /// safe when the derived buffer overrides that method with a genuinely durable non-transactional write. The
    /// base implementation of that method also throws, so the override must be provided.
    /// </summary>
    Warn,
}
