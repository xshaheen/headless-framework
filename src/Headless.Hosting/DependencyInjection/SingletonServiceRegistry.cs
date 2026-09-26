// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// Collects <c>RequireSingletonService</c> declarations together with the collection they were declared on. The
/// collection is kept, not snapshotted, because the lifetime that matters is the one registered by the time the host
/// starts, and the application usually registers the service after the feature declared its requirement.
/// </summary>
internal sealed class SingletonServiceRegistry(IServiceCollection services)
{
    private readonly List<RequiredServiceRegistration> _ordered = [];
    private readonly HashSet<RequiredServiceRegistration> _seen = [];
    private readonly Lock _gate = new();

    public IServiceCollection Services { get; } = services;

    public void Add(RequiredServiceRegistration registration)
    {
        lock (_gate)
        {
            if (_seen.Add(registration))
            {
                _ordered.Add(registration);
            }
        }
    }

    public IReadOnlyList<RequiredServiceRegistration> Registrations
    {
        get
        {
            lock (_gate)
            {
                return [.. _ordered];
            }
        }
    }
}
