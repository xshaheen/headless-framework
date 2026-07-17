// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// In-memory conformance fixture: every scope is backed by a real <see cref="CommitCoordinator" />
/// over a fresh <see cref="CommitScopeStack" />, with an empty service provider captured for drain.
/// </summary>
public sealed class InMemoryCommitCoordinationFixture : ICommitCoordinationFixture, IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public IServiceProvider Services => _services;

    public ValueTask<ICommitScope> BeginScopeAsync(CancellationToken cancellationToken)
    {
        var factory = new CommitScopeFactory(new CommitScopeStack());

        return ValueTask.FromResult(factory.Begin(_services));
    }

    public ICurrentCommitCoordinator CreateStack()
    {
        return new CommitScopeStack();
    }

    public void Dispose()
    {
        _services.Dispose();
    }
}
