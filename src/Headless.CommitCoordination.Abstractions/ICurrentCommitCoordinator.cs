// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Provides read-only ambient access to the innermost active commit coordinator for the current async execution
/// context.
/// </summary>
/// <remarks>
/// The coordinator is tracked in an <see cref="System.Threading.AsyncLocal{T}" /> stack: it flows into child
/// tasks and continuations but changes made in child contexts do not propagate back to parents. A
/// <see langword="null" /> value means no commit coordination scope is active in the current context.
/// </remarks>
[PublicAPI]
public interface ICurrentCommitCoordinator
{
    /// <summary>
    /// Gets the innermost active coordinator, or <see langword="null" /> when no commit coordination scope is
    /// open in the current async execution context.
    /// </summary>
    ICommitCoordinator? Current { get; }
}
