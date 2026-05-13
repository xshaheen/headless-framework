// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fixtures;

/// <summary>
/// Composes reusable transport and storage fixtures into a single test stack fixture.
/// </summary>
[PublicAPI]
public abstract class MessagingStackFixtureBase : IAsyncLifetime
{
    private IReadOnlyList<IAsyncLifetime> _components = [];

    protected void RegisterComponents(params IAsyncLifetime[] components)
    {
        _components = components;
    }

    public async ValueTask InitializeAsync()
    {
        foreach (var component in _components)
        {
            await component.InitializeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _components.Count - 1; index >= 0; index--)
        {
            await _components[index].DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
