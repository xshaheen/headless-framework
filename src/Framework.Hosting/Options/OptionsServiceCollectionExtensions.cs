using FluentValidation;
using Framework.Hosting.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class OptionsServiceCollectionExtensions
{
    #region Configure Singleton

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.</summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        return services
            .Configure<TOptions>(configuration, configureBinder)
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value);
    }

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.</summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions>(
        this IServiceCollection services,
        Action<TOptions> configure
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = services.AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value).Configure(configure);

        return services;
    }

    #endregion

    #region Configure Singleton & Validate Data Annotations

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
        Func<TOptions, bool>? validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateDataAnnotations();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configure">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOptions>(
        this IServiceCollection services,
        Action<TOptions> configure,
        Func<TOptions, bool>? validation
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Configure(configure)
            .ValidateDataAnnotations();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    #endregion

    #region Configure Singleton & Validate DI Fluent Validation

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
        Func<TOptions, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <param name="services">The services collection.</param>
    /// <param name="configure">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateFluentValidation<TOptions>(
        this IServiceCollection services,
        Action<TOptions> configure,
        Func<TOptions, bool>? validation = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Configure(configure)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    #endregion

    #region Configure & Validate Specific Fluent Validator

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection Configure<TOptions, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        string? name = null,
        Func<TOptions, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        var builder = services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddOptions<TOptions>(name)
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration.</param>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection Configure<TOptions, TOptionValidator>(
        this IServiceCollection services,
        Action<TOptions> configure,
        string? name = null,
        Func<TOptions, bool>? validation = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddOptions<TOptions>(name)
            .Configure(configure)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    #endregion

    #region Configure Singleton & Validate Specific Fluent Validator

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<TOptions, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configuration);

        var builder = services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(configuration, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOptions, TOptionValidator>(
        this IServiceCollection services,
        Action<TOptions> configure,
        Func<TOptions, bool>? validation = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = services
            .AddSingleton<IValidator<TOptions>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Configure(configure)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    #endregion
}
