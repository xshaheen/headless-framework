// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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

    public static void Replace<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(TService))
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

    public static bool Unregister<TService>(this IServiceCollection services)
    {
        Argument.IsNotNull(services);

        var unregistered = false;

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
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
}
