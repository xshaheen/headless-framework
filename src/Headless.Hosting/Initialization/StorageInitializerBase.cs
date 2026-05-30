// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;

namespace Headless.Hosting.Initialization;

/// <summary>
/// Base class for storage initializers that need <see cref="IHostedLifecycleService"/> +
/// <see cref="IInitializer"/> with TCS-based completion semantics. Subclasses override
/// <see cref="InitializeAsync"/> with their DDL/schema work; this base owns lifecycle plumbing,
/// host-restart re-entry, and the wait-for-completion promise.
/// </summary>
/// <remarks>
/// On a host restart, the completion source is swapped atomically and the previous promise is
/// canceled with <see cref="CancellationToken.None"/> so waiters from the prior run observe
/// <see cref="OperationCanceledException"/> rather than hanging. On first start the field
/// initializer's TCS is never <c>IsCompleted</c>, so the cancel path is skipped.
/// </remarks>
[PublicAPI]
public abstract class StorageInitializerBase : IHostedLifecycleService, IInitializer
{
    private volatile TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (_completion.Task.IsCompleted)
        {
            var previous = Interlocked.Exchange(
                ref _completion,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            // Pass CancellationToken.None so the prior promise's OperationCanceledException is not
            // misleadingly attributed to the current run's startup token.
            previous.TrySetCanceled(CancellationToken.None);
        }

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            IsInitialized = true;
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
