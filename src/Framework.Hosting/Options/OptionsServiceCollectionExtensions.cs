using FluentValidation;
using Framework.Hosting.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class OptionsServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.</summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        return services
            .Configure<TOptions>(configuration)
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value);
    }

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.</summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BinderOptions> configureBinder
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        return services
            .Configure<TOptions>(configuration, configureBinder)
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value);
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<TOptions, bool> validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);
        Argument.IsNotNull(validation);

        services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateDataAnnotations()
            .Validate(validation)
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndFluentValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateFluentValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<TOptions, bool> validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);
        Argument.IsNotNull(validation);

        services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation()
            .Validate(validation)
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<TOptions, bool> validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);
        Argument.IsNotNull(validation);

        services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation()
            .Validate(validation)
            .ValidateOnStart();

        return services;
    }
}
