// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Carries the owner-supplied data needed to open and attach a new commit coordination scope inside an
/// <see cref="ICommitSignalSource" />.
/// </summary>
/// <remarks>
/// This is a parameter object — constructed by the enlistment helper (e.g. <c>EnlistCommitCoordination</c>)
/// and forwarded to <see cref="ICommitSignalSource.Attach" />. The signal source opens a scope via
/// <see cref="ICommitScopeFactory" /> and uses <see cref="ProviderTransactionKey" /> to correlate the scope
/// to a later commit or rollback edge.
/// </remarks>
[PublicAPI]
public sealed class CommitCoordinatorBindings
{
    /// <summary>
    /// Gets the service provider from the enlistment call site, kept alive for the scope's callback drain.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the provider capabilities to attach to the new coordinator root. The enlistment helper constructs
    /// these (for example a relational helper supplies an <see cref="IRelationalCommitContext" />); the signal
    /// source forwards them untouched to <see cref="ICommitScopeFactory.Begin" />. <see langword="null" /> or
    /// empty for flows that expose no capability. Keeping this a collection of opaque markers preserves the
    /// binding's datastore-agnostic contract — no specific provider handle is hard-coded here.
    /// </summary>
    public IReadOnlyCollection<ICommitCapability>? Capabilities { get; init; }

    /// <summary>
    /// Gets the provider-specific key used by the signal source to correlate a later commit or rollback
    /// diagnostic/interceptor event to this scope. This is a correlation handle only — capabilities are carried
    /// in <see cref="Capabilities" />, never here. For example, EF Core uses the <c>DbTransaction</c> instance;
    /// SQL Server uses the connection's <c>ClientConnectionId</c>.
    /// </summary>
    public object? ProviderTransactionKey { get; init; }
}
