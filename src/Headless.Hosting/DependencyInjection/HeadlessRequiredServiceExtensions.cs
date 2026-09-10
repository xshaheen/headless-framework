// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Declares services that a feature consumes but does not register itself, so the host fails at startup
/// instead of at first use.
/// </summary>
/// <remarks>
/// The abstraction-plus-provider split means a package can compile and register cleanly against a contract
/// whose only implementation ships in a provider package the host must choose (an abstractions-only package
/// consuming <c>ICache&lt;T&gt;</c>, for example). Without a declared requirement such a host starts green
/// and throws on the first request that touches the feature. Requirements declared here are collected across
/// every feature in the host and checked once, before any hosted service starts.
/// </remarks>
[PublicAPI]
public static class HeadlessRequiredServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Declares that <typeparamref name="T"/> must be registered by the time the host starts, and fails
        /// the host with an actionable message if it is not.
        /// </summary>
        /// <typeparam name="T">The service contract this feature resolves but does not register.</typeparam>
        /// <param name="requiredBy">
        /// User-facing name of the feature that needs it, for example <c>"Headless settings value caching"</c>.
        /// Name the capability an operator recognises, not the internal class that injects the contract.
        /// </param>
        /// <param name="remedy">
        /// The concrete call that satisfies the requirement, for example
        /// <c>"Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."</c>.
        /// Quoted verbatim in the failure message.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// The check runs at host start, not here: the requirement is usually satisfied by a sibling
        /// <c>Add…</c> call that has not run yet when this one does, so inspecting the collection at
        /// declaration time would reject valid registration orders.
        /// </remarks>
        /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="requiredBy"/> or <paramref name="remedy"/> is empty or whitespace.</exception>
        public IServiceCollection RequireRegisteredService<T>(string requiredBy, string remedy)
            where T : class
        {
            return services.RequireRegisteredService(typeof(T), requiredBy, remedy);
        }

        /// <summary>
        /// Declares that <paramref name="serviceType"/> must be registered by the time the host starts, and
        /// fails the host with an actionable message if it is not.
        /// </summary>
        /// <param name="serviceType">The service contract this feature resolves but does not register.</param>
        /// <param name="requiredBy">User-facing name of the feature that needs it.</param>
        /// <param name="remedy">The concrete call that satisfies the requirement, quoted verbatim in the failure message.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="requiredBy"/> or <paramref name="remedy"/> is empty or whitespace.</exception>
        public IServiceCollection RequireRegisteredService(Type serviceType, string requiredBy, string remedy)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(serviceType);
            Argument.IsNotNullOrWhiteSpace(requiredBy);
            Argument.IsNotNullOrWhiteSpace(remedy);

            // Idempotent by impl type, so the runner is registered exactly once no matter how many features
            // declare requirements.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RequiredServiceStartupValidator>());

            _GetOrAddRegistry(services).Add(new RequiredServiceRegistration(serviceType, requiredBy, remedy));

            return services;
        }
    }

    /// <summary>
    /// Returns the registry instance already registered on this collection, or registers a fresh one. The
    /// singleton is registered as an instance precisely so later <c>Add…</c> calls can keep appending to the
    /// object the container will hand to <see cref="RequiredServiceStartupValidator"/>.
    /// </summary>
    private static RequiredServiceRegistry _GetOrAddRegistry(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(RequiredServiceRegistry)
        );

        if (existing?.ImplementationInstance is RequiredServiceRegistry registry)
        {
            return registry;
        }

        registry = new RequiredServiceRegistry();
        services.AddSingleton(registry);

        return registry;
    }
}
