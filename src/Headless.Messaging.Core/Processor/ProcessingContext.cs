// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Processor;

public sealed class ProcessingContext(IServiceProvider provider, CancellationToken cancellationToken) : IDisposable
{
    private IServiceScope? _scope;

    private ProcessingContext(ProcessingContext other)
        : this(other.Provider, other.CancellationToken) { }

    public IServiceProvider Provider { get; private init; } = provider;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public bool IsStopping => CancellationToken.IsCancellationRequested;

    public void Dispose() => _scope?.Dispose();

    public void ThrowIfStopping()
    {
        CancellationToken.ThrowIfCancellationRequested();
    }

    public ProcessingContext CreateScope()
    {
        var serviceScope = Provider.CreateScope();

        return new ProcessingContext(this) { _scope = serviceScope, Provider = serviceScope.ServiceProvider };
    }

    public Task WaitAsync(TimeSpan timeout)
    {
        return Task.Delay(timeout, CancellationToken);
    }
}
