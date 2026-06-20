// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary><see cref="IServiceCollection"/> extension methods.</summary>
[PublicAPI]
public static class DependencyInjectionExtensions
{
    #region AddIf

    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally configure the MVC services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to configure the MVC services.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection AddIf(
        this IServiceCollection services,
        bool condition,
        Func<IServiceCollection, IServiceCollection> action
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(action);

        if (condition)
        {
            services = action(services);
        }

        return services;
    }

    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally configure the MVC services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="condition">If <see langword="true"/> is returned the action is executed.</param>
    /// <param name="action">The action used to configure the MVC services.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection AddIf(
        this IServiceCollection services,
        Func<IServiceCollection, bool> condition,
        Func<IServiceCollection, IServiceCollection> action
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(condition);
        Argument.IsNotNull(action);

        if (condition(services))
        {
            services = action(services);
        }

        return services;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally
    /// configure the MVC services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to configure the MVC services if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to configure the MVC services if the condition is <see langword="false"/>.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection AddIfElse(
        this IServiceCollection services,
        bool condition,
        Func<IServiceCollection, IServiceCollection> ifAction,
        Func<IServiceCollection, IServiceCollection> elseAction
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(services) : elseAction(services);
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally
    /// configure the MVC services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="condition">If <see langword="true"/> is returned the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to configure the MVC services if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to configure the MVC services if the condition is <see langword="false"/>.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection AddIfElse(
        this IServiceCollection services,
        Func<IServiceCollection, bool> condition,
        Func<IServiceCollection, IServiceCollection> ifAction,
        Func<IServiceCollection, IServiceCollection> elseAction
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(condition);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition(services) ? ifAction(services) : elseAction(services);
    }

    #endregion

    #region Decorate

    /// <summary>
    /// Decorates all existing unkeyed registrations for <typeparamref name="TService"/> with
    /// <typeparamref name="TDecorator"/>, preserving each original registration's lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to decorate.</typeparam>
    /// <typeparam name="TDecorator">The decorator type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no unkeyed registration exists for <typeparamref name="TService"/>.
    /// </exception>
    public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        if (!services.TryDecorate<TService, TDecorator>())
        {
            throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered.");
        }

        return services;
    }

    /// <summary>
    /// Decorates all existing unkeyed registrations for <typeparamref name="TService"/> with
    /// <paramref name="decorator"/>, preserving each original registration's lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to decorate.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="decorator">Factory that receives the original service and returns the decorator.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no unkeyed registration exists for <typeparamref name="TService"/>.
    /// </exception>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decorator
    )
        where TService : class
    {
        if (!services.TryDecorate(decorator))
        {
            throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered.");
        }

        return services;
    }

    /// <summary>
    /// Attempts to decorate all existing unkeyed registrations for <typeparamref name="TService"/>
    /// with <typeparamref name="TDecorator"/>, preserving each original registration's lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to decorate.</typeparam>
    /// <typeparam name="TDecorator">The decorator type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns><see langword="true"/> if at least one registration was decorated; otherwise <see langword="false"/>.</returns>
    public static bool TryDecorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        Argument.IsNotNull(services);

        return _TryDecorate(
            services,
            typeof(TService),
            descriptor =>
                _CreateDecoratedDescriptor<TService>(
                    descriptor,
                    (inner, serviceProvider) => ActivatorUtilities.CreateInstance<TDecorator>(serviceProvider, inner)
                )
        );
    }

    /// <summary>
    /// Attempts to decorate all existing unkeyed registrations for <typeparamref name="TService"/>
    /// with <paramref name="decorator"/>, preserving each original registration's lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to decorate.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="decorator">Factory that receives the original service and returns the decorator.</param>
    /// <returns><see langword="true"/> if at least one registration was decorated; otherwise <see langword="false"/>.</returns>
    public static bool TryDecorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decorator
    )
        where TService : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(decorator);

        return _TryDecorate(
            services,
            typeof(TService),
            descriptor => _CreateDecoratedDescriptor(descriptor, decorator)
        );
    }

    private static bool _TryDecorate(
        IServiceCollection services,
        Type serviceType,
        Func<ServiceDescriptor, ServiceDescriptor> createDescriptor
    )
    {
        var decorated = false;

        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];

            if (descriptor.IsKeyedService || descriptor.ServiceType != serviceType)
            {
                continue;
            }

            services[i] = createDescriptor(descriptor);
            decorated = true;
        }

        return decorated;
    }

    private static ServiceDescriptor _CreateDecoratedDescriptor<TService>(
        ServiceDescriptor descriptor,
        Func<TService, IServiceProvider, TService> decorator
    )
        where TService : class
    {
        return ServiceDescriptor.Describe(
            typeof(TService),
            serviceProvider =>
            {
                var inner = _CreateService<TService>(serviceProvider, descriptor);

                return decorator(inner, serviceProvider);
            },
            descriptor.Lifetime
        );
    }

    private static TService _CreateService<TService>(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (TService)descriptor.ImplementationFactory(serviceProvider);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException($"Service '{typeof(TService).Name}' registration cannot be decorated.");
    }

    #endregion

    #region Replace

    /// <summary>Adds or replaces a scoped service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddScoped<TService, TImplementation>();

        return result;
    }

    /// <summary>Adds or replaces a scoped service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationFactory">The factory to create the service implementation.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceScoped<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        var result = services.Unregister<TService>();
        services.AddScoped(implementationFactory);

        return result;
    }

    /// <summary>Adds or replaces a transient service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceTransient<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddTransient<TService, TImplementation>();

        return result;
    }

    /// <summary>Adds or replaces a transient service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationFactory">The factory to create the service implementation.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceTransient<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        var result = services.Unregister<TService>();
        services.AddTransient(implementationFactory);

        return result;
    }

    /// <summary>Adds or replaces a singleton service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceSingleton<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddSingleton<TService, TImplementation>();

        return result;
    }

    /// <summary>Adds or replaces a singleton service in the service collection.</summary>
    /// <typeparam name="TService">The type of the service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationFactory">The factory to create the service implementation.</param>
    /// <returns>True if the service was replaced, otherwise false.</returns>
    public static bool AddOrReplaceSingleton<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        var result = services.Unregister<TService>();
        services.AddSingleton(implementationFactory);

        return result;
    }

    /// <summary>
    /// Removes fallback singleton registrations for <typeparamref name="TService"/> and adds
    /// <typeparamref name="TImplementation"/> only when no non-fallback registration remains.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    /// <typeparam name="TFallback">The fallback implementation type to remove.</typeparam>
    /// <typeparam name="TImplementation">The default implementation type to add when needed.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>True if one or more fallback registrations were removed; otherwise false.</returns>
    /// <remarks>
    /// This is useful when one package registers a safe fallback and another package can provide a
    /// stronger default without overwriting consumer-provided implementations. Only type-based
    /// (<c>AddSingleton&lt;TService, TFallback&gt;()</c>) and instance-based fallback registrations are
    /// detected; a factory-registered fallback (<c>AddSingleton&lt;TService&gt;(_ =&gt; new TFallback())</c>)
    /// is indistinguishable from a consumer override here and is intentionally preserved. Register
    /// fallbacks by type to make them replaceable.
    /// </remarks>
    public static bool AddOrReplaceFallbackSingleton<TService, TFallback, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TFallback : class, TService
        where TImplementation : class, TService
    {
        Argument.IsNotNull(services);

        var replaced = false;

        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];

            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(TService))
            {
                continue;
            }

            if (descriptor.ImplementationType == typeof(TFallback) || descriptor.ImplementationInstance is TFallback)
            {
                services.RemoveAt(i);
                replaced = true;
            }
        }

        services.TryAddSingleton<TService, TImplementation>();

        return replaced;
    }

    /// <summary>
    /// Replaces the existing registration for <typeparamref name="TService"/> with one backed by
    /// <paramref name="factory"/>, preserving the original lifetime.
    /// </summary>
    /// <typeparam name="TService">The service type to replace.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Factory that creates the new implementation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no prior registration exists for <typeparamref name="TService"/>.
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="AddOrReplaceSingleton{TService,TImplementation}(IServiceCollection)"/>,
    /// <see cref="AddOrReplaceScoped{TService,TImplementation}(IServiceCollection)"/>, and
    /// <see cref="AddOrReplaceTransient{TService,TImplementation}(IServiceCollection)"/>, this method
    /// throws instead of adding when no prior registration exists. Use it to enforce that the consumer
    /// already configured the service.
    /// </remarks>
    public static void Replace<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(factory);

        for (var i = 0; i < services.Count; i++)
        {
            // Skip keyed registrations: overwriting a keyed slot with a non-keyed descriptor would
            // corrupt it (matches the IsKeyedService guard in _TryDecorate / AddOrReplaceFallbackSingleton).
            if (!services[i].IsKeyedService && services[i].ServiceType == typeof(TService))
            {
                var lifetime = services[i].Lifetime;
                services[i] = new ServiceDescriptor(typeof(TService), factory, lifetime);
                return;
            }
        }

        throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered.");
    }

    #endregion

    #region Unregister

    /// <summary>
    /// Removes all unkeyed registrations for <typeparamref name="TService"/>. Keyed registrations are
    /// left intact, matching the <see cref="ServiceDescriptor.IsKeyedService"/> guard in
    /// <see cref="TryDecorate{TService,TDecorator}"/> and
    /// <see cref="AddOrReplaceFallbackSingleton{TService,TFallback,TImplementation}"/>.
    /// </summary>
    /// <typeparam name="TService">The service type to remove.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns><see langword="true"/> if at least one registration was removed; otherwise <see langword="false"/>.</returns>
    public static bool Unregister<TService>(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var unregistered = false;

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService && services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
                unregistered = true;
            }
        }

        return unregistered;
    }

    #endregion

    #region IsAdded

    public static bool IsAdded<T>(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        return services.IsAdded(typeof(T));
    }

    public static bool IsAdded(this IServiceCollection services, Type type)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(type);

        return services.Any(d => d.ServiceType == type);
    }

    #endregion

    #region Keyed Services

    public static IServiceCollection AddKeyedSingleton<TService>(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(implementationFactory);

        return services.AddKeyedSingleton<TService>(serviceKey, (provider, _) => implementationFactory(provider));
    }

    public static IServiceCollection AddKeyedScoped<TService>(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(implementationFactory);

        return services.AddKeyedScoped<TService>(serviceKey, (provider, _) => implementationFactory(provider));
    }

    public static IServiceCollection AddKeyedTransient<TService>(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(implementationFactory);

        return services.AddKeyedTransient<TService>(serviceKey, (provider, _) => implementationFactory(provider));
    }
    #endregion

    #region Hosted Service

    /// <summary>
    /// Removes a hosted service of type <typeparamref name="T"/> from the service collection.
    /// </summary>
    /// <typeparam name="T">The type of the hosted service to remove.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// This method only removes services registered via <c>AddHostedService&lt;T&gt;()</c> with a type parameter.
    /// Services registered with a factory delegate (e.g., <c>AddHostedService(sp => new T(...))</c>) will NOT be removed
    /// because <see cref="ServiceDescriptor.ImplementationType"/> is null for factory-based registrations.
    /// </remarks>
    public static IServiceCollection RemoveHostedService<T>(this IServiceCollection services)
        where T : IHostedService
    {
        Argument.IsNotNull(services);

        var hostedServiceDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(T)
        );

        if (hostedServiceDescriptor is not null)
        {
            services.Remove(hostedServiceDescriptor);
        }

        return services;
    }

    #endregion

    #region AddInitializerHostedService

    /// <summary>
    /// Registers <typeparamref name="T"/> as a singleton, forwards it as <see cref="IInitializer"/>,
    /// and registers it as a hosted service — all using the same singleton instance. When
    /// <typeparamref name="T"/> also implements <see cref="IHostedLifecycleService"/>, the .NET
    /// host detects that interface on the registered <see cref="IHostedService"/> entry and
    /// invokes the <c>StartingAsync</c> / <c>StartedAsync</c> / <c>StoppingAsync</c> /
    /// <c>StoppedAsync</c> hooks automatically — no extra registration is required.
    /// </summary>
    /// <typeparam name="T">
    /// A type that implements both <see cref="IHostedService"/> and <see cref="IInitializer"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Using <c>TryAddSingleton</c> and <c>TryAddEnumerable</c> guards against double-registration
    /// when the setup method is called more than once. The <c>ImplementationType</c> carried by the
    /// two-argument overload of <c>ServiceDescriptor.Singleton&lt;TService, TImplementation&gt;</c>
    /// is what allows <c>TryAddEnumerable</c> to deduplicate by implementation type.
    /// </remarks>
    public static IServiceCollection AddInitializerHostedService<T>(this IServiceCollection services)
        where T : class, IHostedService, IInitializer
    {
        Argument.IsNotNull(services);

        services.TryAddSingleton<T>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInitializer, T>(sp => sp.GetRequiredService<T>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, T>(sp => sp.GetRequiredService<T>()));

        // No separate IHostedLifecycleService registration is required. .NET's hosting
        // infrastructure iterates registered IHostedService instances and pattern-matches each on
        // IHostedLifecycleService — so an initializer that implements IHostedLifecycleService gets
        // its StartingAsync/StartedAsync/StoppingAsync/StoppedAsync hooks invoked automatically.

        return services;
    }

    #endregion
}
