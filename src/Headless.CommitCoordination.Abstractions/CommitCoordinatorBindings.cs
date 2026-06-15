// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// Gets the opaque provider capabilities attached to the new coordinator. The scope owner builds these
    /// (for example a relational owner supplies an <see cref="IRelationalCommitContext" />); the signal source
    /// forwards them untouched. Null or empty for flows that expose no capability. This keeps the binding
    /// datastore-agnostic — no relational handle is hard-coded on the contract itself.
    /// </summary>
    public IReadOnlyCollection<ICommitCapability>? Capabilities { get; init; }

    /// <summary>
    /// Gets the provider-specific transaction correlation key when available. This is a correlation key only —
    /// not a transport for capability handles.
    /// </summary>
    public object? ProviderTransactionKey { get; init; }
}
