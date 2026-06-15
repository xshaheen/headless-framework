// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Opens ambient commit coordination scopes. This is the scope-opening primitive for
/// <see cref="ICommitSignalSource" /> implementations: a signal source resolves it from DI and calls
/// <see cref="Begin" /> when attaching a unit of work. Consumers never open scopes directly — they observe the
/// ambient coordinator through <see cref="ICurrentCommitCoordinator" /> and enlist via provider extensions.
/// </summary>
[PublicAPI]
public interface ICommitScopeFactory
{
    /// <summary>
    /// Opens a scope, joining the current root coordinator when one exists (child work is promoted to the root;
    /// a child rollback dooms the root).
    /// </summary>
    /// <param name="services">The service provider captured for callback drain.</param>
    /// <param name="capabilities">Capabilities attached when a new root is opened.</param>
    /// <returns>The opened scope.</returns>
    ICommitScope Begin(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null);

    /// <summary>
    /// Opens an independent root coordinator even when an ambient coordinator exists (e.g. a background unit of
    /// work that must not join the caller's transaction).
    /// </summary>
    /// <param name="services">The service provider captured for callback drain.</param>
    /// <param name="capabilities">Capabilities attached to the new root.</param>
    /// <returns>The opened scope.</returns>
    ICommitScope BeginNew(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null);
}
