// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>
/// Collects <c>RequireSingletonService</c> declarations together with the collection they were declared on. The
/// collection is kept, not snapshotted, because the lifetime that matters is the one registered by the time the host
/// starts, and the application usually registers the service after the feature declared its requirement.
/// </summary>
internal sealed class SingletonServiceRegistry(IServiceCollection services)
{
    private readonly RequiredServiceRegistry _registrations = new();

    public IServiceCollection Services { get; } = services;

    public IReadOnlyList<RequiredServiceRegistration> Registrations => _registrations.Registrations;

    public void Add(RequiredServiceRegistration registration)
    {
        _registrations.Add(registration);
    }
}
