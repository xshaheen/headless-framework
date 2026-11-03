// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Processor;

internal sealed class ProcessingContext(
    IServiceProvider provider,
    TimeProvider timeProvider,
    CancellationToken cancellationToken
) : IAsyncDisposable
{
    private AsyncServiceScope? _scope;

    private ProcessingContext(ProcessingContext other)
        : this(other.Provider, other.TimeProvider, other.CancellationToken) { }

    /// <summary>The app clock the processors schedule on.</summary>
    public TimeProvider TimeProvider { get; } = timeProvider;

    public IServiceProvider Provider { get; private init; } = provider;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public bool IsStopping => CancellationToken.IsCancellationRequested;

    public async ValueTask DisposeAsync()
    {
        if (_scope is { } scope)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void ThrowIfStopping()
    {
        CancellationToken.ThrowIfCancellationRequested();
    }

    public ProcessingContext CreateScope()
    {
        var serviceScope = Provider.CreateAsyncScope();

        return new ProcessingContext(this) { _scope = serviceScope, Provider = serviceScope.ServiceProvider };
    }

    public Task WaitAsync(TimeSpan timeout)
    {
        return TimeProvider.Delay(timeout, CancellationToken);
    }

    public DateTimeOffset GetUtcNow()
    {
        return TimeProvider.GetUtcNow();
    }
}
