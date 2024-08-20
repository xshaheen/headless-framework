#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary><see cref="IServiceCollection"/> extension methods.</summary>
public static class DependencyInjectionExtensions
{
    #region AddIf

    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally configure the MVC services.
    /// </summary>
    /// <param name="services">The services collection.</param>
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
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally
    /// configure the MVC services.
    /// </summary>
    /// <param name="services">The services collection.</param>
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

    #endregion

    #region Replace

    public static bool ReplaceScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddScoped<TService, TImplementation>();

        return result;
    }

    public static bool ReplaceScoped<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        var result = services.Unregister<TService>();
        services.AddScoped(implementationFactory);

        return result;
    }

    public static bool ReplaceTransient<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddTransient<TService, TImplementation>();

        return result;
    }

    public static bool ReplaceTransient<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory
    )
        where TService : class
    {
        var result = services.Unregister<TService>();
        services.AddTransient(implementationFactory);

        return result;
    }

    public static bool ReplaceSingleton<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var result = services.Unregister<TService>();
        services.AddSingleton<TService, TImplementation>();

        return result;
    }

    public static bool ReplaceSingleton<TService>(
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
        var descriptor = services.Single(descriptor => descriptor.ServiceType == typeof(TService));

        var newServiceDescriptor = new ServiceDescriptor(
            serviceType: typeof(TService),
            factory: factory,
            lifetime: descriptor.Lifetime
        );

        services.Remove(descriptor);
        services.Add(newServiceDescriptor);
    }

    #endregion

    #region Unregister

    public static bool Unregister<TService>(this IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(TService));

        var unregistered = false;

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);

            unregistered = true;
        }

        return unregistered;
    }

    #endregion

    #region IsAdded

    public static bool IsAdded<T>(this IServiceCollection services)
    {
        return services.IsAdded(typeof(T));
    }

    public static bool IsAdded(this IServiceCollection services, Type type)
    {
        return services.Any(d => d.ServiceType == type);
    }

    #endregion
}
