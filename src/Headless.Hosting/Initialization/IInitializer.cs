// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Initialization;

/// <summary>
/// Defines the contract for a background service that signals when its initialization is complete.
/// </summary>
/// <remarks>
/// Unlike <c>IBootstrapper</c> which requires an explicit imperative trigger, <c>IInitializer</c> uses
/// passive wait semantics — the service starts automatically via <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
/// and callers simply wait for it to finish. Concurrent callers share the same in-flight wait operation.
/// </remarks>
public interface IInitializer
{
    /// <summary>
    /// Gets a value indicating whether initialization has completed successfully.
    /// Returns <c>true</c> only after the initialization sequence finishes without error.
    /// Returns <c>false</c> while initialization is still in progress, if it was skipped, or if it failed.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Asynchronously waits until initialization completes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait. Does not cancel the underlying initialization.</param>
    /// <returns>A task that completes when initialization finishes successfully, or faults if initialization fails permanently.</returns>
    Task WaitForInitializationAsync(CancellationToken cancellationToken = default);
}
