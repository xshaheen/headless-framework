// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines the contract for messaging bootstrapping logic that initializes the system when the application starts.
/// Implementations perform setup tasks such as initializing storage, registering consumers, or preparing the message queue.
/// </summary>
/// <remarks>
/// The bootstrapper is responsible for:
/// <list type="bullet">
/// <item><description>Initializing storage tables or schema if not already present.</description></item>
/// <item><description>Registering consumer subscribers from discovered assemblies.</description></item>
/// <item><description>Starting required messaging processors and verifying they can initialize successfully.</description></item>
/// <item><description>Preparing the system for message publishing and consuming operations.</description></item>
/// </list>
/// The bootstrapper is registered as a hosted service and automatically starts when the application starts.
/// </remarks>
public interface IBootstrapper : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the bootstrap process has completed successfully.
    /// Returns true only after the required messaging startup sequence completes successfully.
    /// Returns false while bootstrap is still in progress, after shutdown begins, or when startup failed.
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// Asynchronously performs the bootstrap initialization for the messaging system.
    /// This method is called when the application starts and should complete all necessary initialization.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous bootstrap operation.</returns>
    /// <remarks>
    /// Concurrent callers await the same in-flight bootstrap operation.
    /// Canceling a later caller's wait does not cancel shared startup unless that caller owns the bootstrap operation.
    /// </remarks>
    Task BootstrapAsync(CancellationToken cancellationToken = default);
}
