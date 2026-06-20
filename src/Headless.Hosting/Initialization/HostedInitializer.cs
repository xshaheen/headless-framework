// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;

namespace Headless.Hosting.Initialization;

/// <summary>
/// Base class for hosted-lifecycle initializers that need <see cref="IHostedLifecycleService"/> +
/// <see cref="IInitializer"/> with TCS-based completion semantics. Subclasses override
/// <see cref="InitializeAsync"/> with their startup work (DDL/schema bootstrap, topology
/// provisioning, warm-up, etc.); this base owns lifecycle plumbing, host-restart re-entry, and the
/// wait-for-completion promise.
/// </summary>
/// <remarks>
/// On a host restart, the completion source is swapped atomically and the previous promise is
/// canceled with <see cref="CancellationToken.None"/> so waiters from the prior run observe
/// <see cref="OperationCanceledException"/> rather than hanging. On first start the field
/// initializer's TCS is never <c>IsCompleted</c>, so the cancel path is skipped.
/// <para>
/// Subclasses may override <see cref="RunOnStartup"/> to return <c>false</c> to skip
/// <see cref="InitializeAsync"/> entirely while still marking the initializer complete, so
/// dependents that await <see cref="WaitForInitializationAsync"/> do not block.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class HostedInitializer : IHostedLifecycleService, IInitializer
{
    private volatile TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a value indicating whether initialization has completed successfully.</summary>
    /// <remarks>
    /// Derived from the completion promise so it can never disagree with it: <c>true</c> only after a
    /// successful (or skipped) run, and <c>false</c> while initialization is still in progress —
    /// including the host-restart re-entry window — or when the run faulted or was canceled. Reading a
    /// stored flag instead would report a stale <c>true</c> across a restart while the new
    /// <see cref="InitializeAsync"/> is still running.
    /// </remarks>
    public bool IsInitialized => _completion.Task.IsCompletedSuccessfully;

    /// <summary>
    /// When <c>false</c>, <see cref="StartingAsync"/> skips <see cref="InitializeAsync"/> entirely
    /// but still marks the initializer complete (<see cref="IsInitialized"/> becomes <c>true</c> and
    /// the completion promise is resolved) so dependents awaiting
    /// <see cref="WaitForInitializationAsync"/> are released. Defaults to <c>true</c>.
    /// </summary>
    protected virtual bool RunOnStartup => true;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (_completion.Task.IsCompleted)
        {
            var previous = Interlocked.Exchange(
                ref _completion,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );

            // Pass CancellationToken.None so the prior promise's OperationCanceledException is not
            // misleadingly attributed to the current run's startup token.
            previous.TrySetCanceled(CancellationToken.None);
        }

        if (!RunOnStartup)
        {
            // Opt-out: skip the actual work but still complete so WaitForInitializationAsync waiters
            // are released. Used when the schema/topology is provisioned out-of-band.
            _completion.TrySetResult();

            return;
        }

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implement the actual initialization work (DDL, schema bootstrap, etc.). Throw to mark
    /// the initializer as failed; the exception flows out of both <see cref="StartingAsync"/>
    /// and <see cref="WaitForInitializationAsync"/>.
    /// </summary>
    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);
}
