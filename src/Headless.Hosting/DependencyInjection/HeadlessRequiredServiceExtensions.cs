// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.DependencyInjection;

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

            // Idempotent by validator type, so one check covers every requirement declared in the host.
            services.AddStartupValidator<RequiredServiceStartupValidator>();

            _GetOrAddRegistry(services, static () => new RequiredServiceRegistry())
                .Add(new RequiredServiceRegistration(serviceType, requiredBy, remedy));

            return services;
        }

        /// <summary>
        /// Declares that <typeparamref name="T"/> must be registered as a singleton by the time the host starts,
        /// and fails the host with an actionable message if it is missing, scoped, or transient.
        /// </summary>
        /// <typeparam name="T">The service contract a singleton in this feature injects.</typeparam>
        /// <param name="requiredBy">User-facing name of the feature whose singleton captures the service.</param>
        /// <param name="remedy">The concrete registration that satisfies the requirement, quoted verbatim in the failure message.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// Use it when a singleton injects a service the application registers. A scoped or transient registration
        /// would be captured by that singleton for the life of the host. Scope validation reports the scoped case
        /// only when it is switched on and never reports the transient one, so this check reads the lifetime of the
        /// registration the container would resolve and refuses in every environment. A missing registration is
        /// reported as a <see cref="MissingRequiredServiceException"/>. The check never resolves the service.
        /// </remarks>
        /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="requiredBy"/> or <paramref name="remedy"/> is empty or whitespace.</exception>
        public IServiceCollection RequireSingletonService<T>(string requiredBy, string remedy)
            where T : class
        {
            return services.RequireSingletonService(typeof(T), requiredBy, remedy);
        }

        /// <summary>
        /// Declares that <paramref name="serviceType"/> must be registered as a singleton by the time the host
        /// starts, and fails the host with an actionable message if it is missing, scoped, or transient.
        /// </summary>
        /// <param name="serviceType">The service contract a singleton in this feature injects.</param>
        /// <param name="requiredBy">User-facing name of the feature whose singleton captures the service.</param>
        /// <param name="remedy">The concrete registration that satisfies the requirement, quoted verbatim in the failure message.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="requiredBy"/> or <paramref name="remedy"/> is empty or whitespace.</exception>
        public IServiceCollection RequireSingletonService(Type serviceType, string requiredBy, string remedy)
        {
            services.RequireRegisteredService(serviceType, requiredBy, remedy);

            // Idempotent by validator type, so one check covers every singleton requirement declared in the host.
            services.AddStartupValidator<SingletonServiceStartupValidator>();

            _GetOrAddRegistry(services, () => new SingletonServiceRegistry(services))
                .Add(new RequiredServiceRegistration(serviceType, requiredBy, remedy));

            return services;
        }
    }

    /// <summary>
    /// Returns the <typeparamref name="TRegistry"/> instance already registered on this collection, or registers the
    /// one <paramref name="create"/> builds. The registry is registered as an instance precisely so later <c>Add…</c>
    /// calls keep appending to the object the container hands to the startup validator.
    /// </summary>
    private static TRegistry _GetOrAddRegistry<TRegistry>(IServiceCollection services, Func<TRegistry> create)
        where TRegistry : class
    {
        var existing = services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(TRegistry));

        if (existing?.ImplementationInstance is TRegistry registry)
        {
            return registry;
        }

        registry = create();
        services.AddSingleton(registry);

        return registry;
    }
}
