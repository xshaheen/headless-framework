// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>
/// Fails the host at startup when a service declared through <c>IServiceCollection.RequireSingletonService&lt;T&gt;(…)</c>
/// is registered as scoped or transient.
/// </summary>
/// <remarks>
/// A singleton that injects a scoped or transient service keeps the first instance it was given for the life of the
/// host. Scope validation catches the scoped case only while it is switched on (the development default, off in
/// production) and never catches the transient one, so this check reads the registered lifetime from the service
/// collection instead and refuses the same way in every environment. It never resolves the service.
/// </remarks>
internal sealed class SingletonServiceStartupValidator(SingletonServiceRegistry registry) : IHeadlessStartupValidator
{
    /// <inheritdoc/>
    /// <exception cref="InvalidServiceLifetimeException">One or more declared services are not singletons.</exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        var violations = new List<ServiceLifetimeViolation>();

        foreach (var registration in registry.Registrations)
        {
            if (
                _EffectiveLifetime(registry.Services, registration.ServiceType) is { } lifetime
                && lifetime != ServiceLifetime.Singleton
            )
            {
                violations.Add(new ServiceLifetimeViolation(registration, lifetime));
            }
        }

        if (violations.Count == 0)
        {
            return Task.CompletedTask;
        }

        var lines = violations.Select(violation =>
            $"  - {violation.Requirement.ServiceType.GetFriendlyTypeName()} is registered as {violation.Lifetime} (required as a singleton by {violation.Requirement.RequiredBy}): {violation.Requirement.Remedy}"
        );

        var message =
            $"Headless startup validation failed: {violations.Count} service registration(s) must be singletons."
            + Environment.NewLine
            + string.Join(Environment.NewLine, lines);

        throw new InvalidServiceLifetimeException(message, violations);
    }

    /// <summary>
    /// Returns the lifetime of the registration the container would resolve for <paramref name="serviceType"/>: the
    /// last unkeyed closed registration, otherwise the last unkeyed open-generic registration of its definition.
    /// <see langword="null"/> when nothing is registered, which the required-service check reports.
    /// </summary>
    private static ServiceLifetime? _EffectiveLifetime(IServiceCollection services, Type serviceType)
    {
        var definition = serviceType.IsConstructedGenericType ? serviceType.GetGenericTypeDefinition() : null;
        ServiceLifetime? closed = null;
        ServiceLifetime? open = null;

        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
            {
                continue;
            }

            if (descriptor.ServiceType == serviceType)
            {
                closed = descriptor.Lifetime;
            }
            else if (definition is not null && descriptor.ServiceType == definition)
            {
                open = descriptor.Lifetime;
            }
        }

        return closed ?? open;
    }
}
