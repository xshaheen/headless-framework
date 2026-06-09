// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Captures owner-side data needed to attach a commit signal source to a coordinator.
/// </summary>
[PublicAPI]
public sealed class CommitCoordinatorBindings
{
    /// <summary>
    /// Gets the service provider kept alive until terminal drain completes.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the physical connection used for correlation when available.
    /// </summary>
    public DbConnection? Connection { get; init; }

    /// <summary>
    /// Gets the live transaction surfaced through <see cref="IRelationalCommitContext" /> so participants can
    /// write durable rows inside the unit-of-work at enlist time. Null for non-relational or detached flows.
    /// </summary>
    public DbTransaction? Transaction { get; init; }

    /// <summary>
    /// Gets the provider-specific transaction correlation key when available.
    /// </summary>
    public object? ProviderTransactionKey { get; init; }
}
