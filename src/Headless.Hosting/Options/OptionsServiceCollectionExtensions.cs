// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class OptionsServiceCollectionExtensions
{
    #region Add Option Value & Validator

    public static IServiceCollection AddSingletonOptionValue<TOption>(this IServiceCollection services)
        where TOption : class
    {
        services.TryAddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value);

        return services;
    }

    public static IServiceCollection AddOptionValidator<TOptions, TValidator>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TOptions : class
        where TValidator : class, IValidator<TOptions>
    {
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IValidator<TOptions>), typeof(TValidator), lifetime));

        return services;
    }

    #endregion

    #region Add Options

    /*
     * These methods add the option with a validation and return the OptionsBuilder
     */

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.
    /// Also runs data annotation validation and custom validation using the default failure message on application startup.
    /// </summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="validation">The validation function.</param>
    /// <returns>The same services collection.</returns>
    public static OptionsBuilder<TOption> AddOptions<TOption>(
        this IServiceCollection services,
        string? optionName = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        optionName ??= Options.Options.DefaultName;
        var builder = services.AddOptions<TOption>(optionName);

        if (validation is not null)
        {
            builder.Validate(validation);
            builder.ValidateOnStart();
        }

        return builder;
    }

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
        optionName ??= Options.Options.DefaultName;

        return services
            .AddOptionValidator<TOptions, TOptionValidator>()
            .AddOptions<TOptions>(optionName)
            ._ValidateFunc(validation)
            .ValidateFluentValidation()
            .ValidateOnStart();
    }

    #endregion

    #region Configure

    /// <summary>Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services container.</summary>
    /// <typeparam name="TOption">The type of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOption">The configuration.</param>
    /// <returns>The OptionsBuilder.</returns>
    public static IServiceCollection Configure<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption,
        string? name = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);
        name ??= Options.Options.DefaultName;

        services.AddOptions<TOption>(name).Configure(configureOption)._ValidateFunc(validation);

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
    /// <param name="name">The name of the options instance.</param>
    /// <param name="validation">The validation function.</param>
    /// <param name="configureBinder">Used to configure the binder options.</param>
    /// <returns>The same services collection.</returns>
    public static IServiceCollection Configure<TOption, TOptionValidator>(
        this IServiceCollection services,
        IConfiguration? config,
        string? name = null,
        Func<TOption, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);

        if (config is null && configureBinder is not null)
        {
            throw new ArgumentNullException(
                nameof(config),
                "The configuration must be provided when the binder is configured."
            );
        }

        services.AddOptions<TOption, TOptionValidator>(name, validation)._AddSetupBind(config, configureBinder);

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
    public static IServiceCollection Configure<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption>? setupAction,
        string? name = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        services.AddOptions<TOption, TOptionValidator>(name, validation)._AddSetupAction(setupAction);

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
    public static IServiceCollection Configure<TOption, TOptionValidator>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider>? setupAction,
        string? name = null,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
        where TOptionValidator : class, IValidator<TOption>
    {
        Argument.IsNotNull(services);
        services.AddOptions<TOption, TOptionValidator>(name, validation)._AddSetupAction(setupAction);

        return services;
    }

    #endregion

    #region Configure With Validate Data Annotation

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
    public static IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        IConfiguration config,
        Func<TOption, bool>? validation,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        services
            .AddOptions<TOption>()
            .Bind(config, configureBinder)
            ._ValidateFunc(validation)
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
    public static IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        Action<TOption> configureOption,
        Func<TOption, bool>? validation
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        services
            .AddOptions<TOption>()
            .Configure(configureOption)
            ._ValidateFunc(validation)
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
    public static IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption,
        Func<TOption, bool>? validation
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        services
            .AddOptions<TOption>()
            .Configure(configureOption)
            ._ValidateFunc(validation)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    #endregion

    #region Configure With Validate Fluent Validation

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
    public static IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
        this IServiceCollection services,
        IConfiguration config,
        Func<TOption, bool>? validation = null,
        Action<BinderOptions>? configureBinder = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(config);

        services
            .AddOptions<TOption>()
            .Bind(config, configureBinder)
            ._ValidateFunc(validation)
            .ValidateFluentValidation()
            .ValidateOnStart();

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
    public static IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
        this IServiceCollection services,
        Action<TOption> configureOption,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        services
            .AddOptions<TOption>()
            .Configure(configureOption)
            ._ValidateFunc(validation)
            .ValidateFluentValidation()
            .ValidateOnStart();

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
    public static IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
        this IServiceCollection services,
        Action<TOption, IServiceProvider> configureOption,
        Func<TOption, bool>? validation = null
    )
        where TOption : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configureOption);

        services
            .AddOptions<TOption>()
            .Configure(configureOption)
            ._ValidateFunc(validation)
            .ValidateFluentValidation()
            .ValidateOnStart();

        return services;
    }

    #endregion

    #region Helpers

    private static OptionsBuilder<TOptions> _ValidateFunc<TOptions>(
        this OptionsBuilder<TOptions> builder,
        Func<TOptions, bool>? validation
    )
        where TOptions : class
    {
        if (validation is not null)
        {
            builder.Validate(validation);
        }

        return builder;
    }

    private static OptionsBuilder<TOption> _AddSetupAction<TOption>(
        this OptionsBuilder<TOption> builder,
        Action<TOption, IServiceProvider>? setupAction
    )
        where TOption : class
    {
        if (setupAction is not null)
        {
            builder.Configure(setupAction);
        }

        return builder;
    }

    private static OptionsBuilder<TOption> _AddSetupAction<TOption>(
        this OptionsBuilder<TOption> builder,
        Action<TOption>? setupAction
    )
        where TOption : class
    {
        if (setupAction is not null)
        {
            builder.Configure(setupAction);
        }

        return builder;
    }

    private static OptionsBuilder<TOption> _AddSetupBind<TOption>(
        this OptionsBuilder<TOption> builder,
        IConfiguration? config,
        Action<BinderOptions>? configureBinder
    )
        where TOption : class
    {
        if (config is not null)
        {
            builder.Bind(config, configureBinder);
        }

        return builder;
    }

    #endregion
}
