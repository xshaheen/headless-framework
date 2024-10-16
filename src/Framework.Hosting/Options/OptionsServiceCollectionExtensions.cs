// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using Framework.Hosting.Options;
using Framework.Kernel.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class OptionsServiceCollectionExtensions
{
    #region Add Options

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOptions"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOptions">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="optionName">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static OptionsBuilder<TOptions> AddOptions<TOptions, TOptionValidator>(
        this IServiceCollection services,
        string? optionName = null,
        Func<TOptions, bool>? validation = null
    )
        where TOptions : class
        where TOptionValidator : class, IValidator<TOptions>
    {
        Argument.IsNotNull(services);

        services.AddSingleton<IValidator<TOptions>, TOptionValidator>();

        var builder = services.AddOptions<TOptions>(optionName).ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static OptionsBuilder<TOption> AddSingletonOptions<TOption, TOptionValidator>(
        this IServiceCollection services,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);

        services.AddSingleton<IValidator<TOption>, TOptionValidator>();
        services.TryAddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value);

        var builder = services.AddOptions<TOption>().ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static OptionsBuilder<TOption> AddSingletonOptions<TOption>(
        this IServiceCollection services,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);

        services.TryAddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value);

        var builder = services.AddOptions<TOption>();

        if (validation is not null)
        {
            builder.Validate(validation);
            builder.ValidateOnStart();
        }

        return builder;
    }

    #endregion

    #region Configure Singleton

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.</summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption>(
        this IServiceCollection services,
        IConfiguration config,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        return services
            .Configure<TOption>(config, configureBinder)
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value);
    }

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.</summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption>(
        this IServiceCollection services,
        Action<TOption>? configureOption
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        return services.AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value).Configure(configureOption);
    }

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.</summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(configureOption);

        return services;
    }

    #endregion

    #region Configure Singleton & Validate Data Annotations

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        IConfiguration config,
        Func<TOption, bool>? validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Bind(config, configureBinder)
            .ValidateDataAnnotations();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        Action<TOption> configureOption,
        Func<TOption, bool>? validation
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(configureOption)
            .ValidateDataAnnotations();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption,
        Func<TOption, bool>? validation
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(configureOption)
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
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateFluentValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration config,
        Func<TOptions, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOptions : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value)
            .AddOptions<TOptions>()
            .Bind(config, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateFluentValidation<TOption>(
        this IServiceCollection services,
        Action<TOption> configureOption,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(configureOption)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingletonAndValidateFluentValidation<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        var builder = services
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(configureOption)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    #endregion

    #region Configure Option & Validate Specific Fluent Validator

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureOptions<TOption, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration config,
        string? name = null,
        Func<TOption, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddOptions<TOption>(name)
            .Bind(config, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureOptions<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption> configureOption,
        string? name = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddOptions<TOption>(name)
            .Configure(configureOption)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="setupAction">The configuration.</param>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureOptions<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> setupAction,
        string? name = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddOptions<TOption>(name)
            .Configure(setupAction)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration config,
        Func<TOption, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Bind(config, configureBinder)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="setupAction">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption> setupAction,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(setupAction)
            .ValidateFluentValidation();

        if (validation is not null)
        {
            builder.Validate(validation);
        }

        builder.ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="setupAction">The configuration.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection ConfigureSingleton<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> setupAction,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        var builder = services
            .AddSingleton<IValidator<TOption>, TOptionValidator>()
            .AddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value)
            .AddOptions<TOption>()
            .Configure(setupAction)
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
