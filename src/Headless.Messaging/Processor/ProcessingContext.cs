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
    private readonly TimeProvider _timeProvider = timeProvider;

    private ProcessingContext(ProcessingContext other)
        : this(other.Provider, other._timeProvider, other.CancellationToken) { }

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
        return _timeProvider.Delay(timeout, CancellationToken);
    }

    public DateTimeOffset GetUtcNow()
    {
        return _timeProvider.GetUtcNow();
    }
}
