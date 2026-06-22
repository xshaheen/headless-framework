// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Bridges a provider-specific transaction lifecycle (commit or rollback edges) to the commit coordination
/// infrastructure by opening a scope and signalling it when the native transaction outcome is detected.
/// </summary>
/// <remarks>
/// Implementations differ by how they observe the provider edge:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Out-of-band (interceptor/diagnostic)</b> — EF Core and SQL Server register an
///       interceptor or diagnostic listener that fires after the physical commit or rollback. The scope is
///       correlated by a provider transaction key (e.g. <c>DbTransaction</c> or <c>ClientConnectionId</c>)
///       and drained automatically; callers do not need to signal the scope themselves.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Inline (caller-driven)</b> — PostgreSQL (Npgsql) exposes no out-of-band diagnostic, so the caller
///       holds the scope returned by <see cref="Attach" />, commits the transaction, and then calls
///       <see cref="ICommitScope.SignalAsync" /> directly. Disposing without signalling is treated as a rollback.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Explicit (in-memory)</b> — The in-memory source (<c>InMemoryCommitSignalSource</c>) is also
///       caller-driven and is used in tests or non-relational flows.
///     </description>
///   </item>
/// </list>
/// </remarks>
[PublicAPI]
public interface ICommitSignalSource
{
    /// <summary>
    /// Attaches a new commit coordination scope to the provider signal source for the transaction described by
    /// <paramref name="bindings" />.
    /// </summary>
    /// <remarks>
    /// This method is <b>synchronous by design</b>: the ambient coordinator is pushed onto an
    /// <see cref="System.Threading.AsyncLocal{T}" /> stack in this frame so the coordinator is visible to
    /// all subsequent work in the same call chain. Performing the push inside an <c>async</c> helper would
    /// strand it in a separate execution context, making <see cref="ICurrentCommitCoordinator.Current" />
    /// return <see langword="null" /> for the caller's subsequent awaits.
    /// </remarks>
    /// <param name="bindings">
    /// The owner-supplied data (service provider, provider transaction key, and optional capabilities) used to
    /// open the scope and correlate it to the native transaction edge.
    /// </param>
    /// <param name="cancellationToken">
    /// Observed only during the synchronous attach (scope open). A pre-cancelled token throws before the scope
    /// is pushed; it does not govern the asynchronous post-commit drain, which always runs to completion.
    /// </param>
    /// <returns>The attached scope; the caller or the out-of-band source signals and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> is already cancelled.</exception>
    /// <exception cref="InvalidOperationException">A scope is already attached for the same provider transaction key.</exception>
    ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken);
}
