// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Opens ambient commit coordination scopes. This is the scope-opening primitive used by
/// <see cref="ICommitSignalSource" /> implementations: a signal source resolves this factory from DI and calls
/// <see cref="Begin" /> or <see cref="BeginNew" /> when attaching a unit of work. Consumers never open scopes
/// directly — they observe the ambient coordinator through <see cref="ICurrentCommitCoordinator" /> and enlist
/// via provider extension methods.
/// </summary>
[PublicAPI]
public interface ICommitScopeFactory
{
    /// <summary>
    /// Opens a scope that joins the current ambient root coordinator when one exists.
    /// </summary>
    /// <remarks>
    /// When an ambient coordinator is active, the new scope becomes a <b>child</b>: callbacks registered on the
    /// child are promoted to the root and drain as part of the root's outcome. A child rollback dooms the root
    /// (signals it with <see cref="CommitOutcome.RolledBack" /> regardless of what the root would otherwise
    /// receive). When no ambient coordinator exists, a new root is opened and the provided
    /// <paramref name="capabilities" /> are attached to it; <paramref name="capabilities" /> are ignored when
    /// joining an existing root.
    /// </remarks>
    /// <param name="services">The service provider captured by the scope for the post-commit callback drain.</param>
    /// <param name="capabilities">
    /// Provider capabilities to attach when a new root is created. Ignored when joining an existing ambient root.
    /// </param>
    /// <returns>The opened scope; the caller must dispose it after the physical transaction completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
    ICommitScope Begin(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null);

    /// <summary>
    /// Opens an independent root coordinator even when an ambient coordinator already exists.
    /// </summary>
    /// <remarks>
    /// Use this when the new unit of work must not participate in an outer transaction — for example, a
    /// background job that starts its own transaction inside a request that already has an active coordinator.
    /// The new scope always becomes a fresh root; any existing ambient coordinator is not affected.
    /// </remarks>
    /// <param name="services">The service provider captured by the scope for the post-commit callback drain.</param>
    /// <param name="capabilities">Provider capabilities to attach to the new root.</param>
    /// <returns>The opened scope; the caller must dispose it after the physical transaction completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
    ICommitScope BeginNew(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null);
}
