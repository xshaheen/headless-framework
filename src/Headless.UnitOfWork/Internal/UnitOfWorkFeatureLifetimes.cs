// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Enforces the feature contract at <see cref="IUnitOfWork.GetFeature{TFeature}" />: a feature resolves from the
/// root provider and is shared by every unit, so it must be registered as a singleton. Scope validation catches
/// a scoped feature only while it is switched on (the development default, off in production), and it never
/// catches a transient one, which the root would hand out fresh per call and never dispose. The check reads the
/// registration's lifetime from the service collection instead, so the refusal is the same in every environment.
/// </summary>
/// <param name="services">
/// The collection <c>AddUnitOfWork()</c> was called on. Read lazily, per feature type, after the host built its
/// provider, so features registered after <c>AddUnitOfWork()</c> are seen too.
/// </param>
internal sealed class UnitOfWorkFeatureLifetimes(IServiceCollection services)
{
    private readonly ConcurrentDictionary<Type, ServiceLifetime?> _lifetimes = new();

    /// <summary>
    /// Throws when <paramref name="featureType" /> is registered with a lifetime other than singleton. A type the
    /// collection does not list (registered through another container, or not at all) passes: the provider's own
    /// lookup decides that case.
    /// </summary>
    /// <exception cref="InvalidOperationException">The feature is registered as scoped or transient.</exception>
    public void ThrowIfNotSingleton(Type featureType)
    {
        var lifetime = _lifetimes.GetOrAdd(
            featureType,
            static (type, services) => _LifetimeOf(type, services),
            services
        );

        if (lifetime is { } registered && registered != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"The unit-of-work feature '{featureType.FullName}' is registered as {registered}, but a feature is resolved from the root provider and shared by every unit, so it must be a singleton. "
                    + "Register it as a singleton and keep per-unit state on the handle (IUnitOfWork.GetOrAdd) and scoped services in the calling scope."
            );
        }
    }

    private static ServiceLifetime? _LifetimeOf(Type type, IServiceCollection services)
    {
        ServiceLifetime? lifetime = null;

        // GetService returns the last unkeyed registration of a type, so that is the lifetime that matters.
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService && descriptor.ServiceType == type)
            {
                lifetime = descriptor.Lifetime;
            }
        }

        return lifetime;
    }
}
