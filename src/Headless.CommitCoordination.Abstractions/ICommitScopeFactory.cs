// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.CommitCoordination;

/// <summary>
/// Opens ambient commit coordination scopes. This is the scope-opening primitive used by provider enlistment
/// helpers: a helper resolves this factory from DI and calls <see cref="Open" /> when a unit of work begins.
/// Consumers never open scopes directly — they observe the ambient coordinator through
/// <see cref="ICurrentCommitCoordinator" /> and enlist via <see cref="ICommitCoordinator.OnCommit" />.
/// </summary>
[PublicAPI]
public interface ICommitScopeFactory
{
    /// <summary>
    /// Opens a new root scope and makes its coordinator ambient.
    /// </summary>
    /// <remarks>
    /// Every call opens an independent root, even when another coordinator is already ambient: the new scope
    /// has its own registrations and outcome, and the previous coordinator becomes ambient again once the new
    /// scope is disposed. The ambient frame is pushed synchronously in the caller's frame, so
    /// <see cref="ICurrentCommitCoordinator.Current" /> returns the new coordinator immediately after this
    /// method returns and flows into every awaited callee. Callers must therefore invoke it directly from the
    /// frame that owns the unit of work, never from inside an <c>async</c> helper, whose execution context is
    /// restored on return and would strand the frame.
    /// </remarks>
    /// <param name="relational">
    /// The live relational connection and transaction to expose as <see cref="ICommitCoordinator.Relational" />,
    /// or <see langword="null" /> for a unit of work that is not bound to a relational transaction.
    /// </param>
    /// <returns>The opened scope; the caller must signal it and dispose it after the physical transaction completes.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ICommitScope Open(IRelationalCommitContext? relational);
}
