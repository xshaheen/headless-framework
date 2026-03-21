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
    extension(IServiceCollection services)
    {
        #region Add Option Value & Validator

        /// <summary>Registers the resolved options value as a singleton service for direct <typeparamref name="TOption"/> injection.</summary>
        /// <typeparam name="TOption">The options type.</typeparam>
        /// <returns>The same service collection.</returns>
        public IServiceCollection AddSingletonOptionValue<TOption>()
            where TOption : class
        {
            services.TryAddSingleton(x => x.GetRequiredService<IOptions<TOption>>().Value);

            return services;
        }

        /// <summary>Registers a FluentValidation validator for an options type.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <typeparam name="TValidator">The validator type.</typeparam>
        /// <param name="lifetime">The service lifetime for the validator registration.</param>
        /// <returns>The same service collection.</returns>
        public IServiceCollection AddOptionValidator<TOptions, TValidator>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TOptions : class
            where TValidator : class, IValidator<TOptions>
        {
            services.TryAddEnumerable(
                new ServiceDescriptor(typeof(IValidator<TOptions>), typeof(TValidator), lifetime)
            );

            return services;
        }

        #endregion

        #region Add Options

        /*
         * These methods add the option with a validation and return the OptionsBuilder
         */

        /// <summary>Registers named options and optionally applies custom validation.</summary>
        /// <typeparam name="TOption">The type of the options.</typeparam>
        /// <param name="optionName">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The created options' builder.</returns>
        public OptionsBuilder<TOption> AddOptions<TOption>(
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

        /// <summary>Registers named options, adds FluentValidation validation, and optionally applies custom validation.</summary>
        /// <typeparam name="TOptions">The type of the options.</typeparam>
        /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
        /// <param name="optionName">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The created options' builder.</returns>
        public OptionsBuilder<TOptions> AddOptions<TOptions, TOptionValidator>(
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

        /// <summary>
        /// Registers named options and applies configuration from a delegate that has access to <see cref="IServiceProvider"/>.
        /// </summary>
        /// <typeparam name="TOption">The type of the options.</typeparam>
        /// <param name="configureOption">The configuration.</param>
        /// <param name="name">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same service collection.</returns>
        public IServiceCollection Configure<TOption>(
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
        /// Registers <see cref="IOptions{TOptions}"/> and <typeparamref name="TOption"/> to the services' container.
        /// Also runs data annotation validation and custom validation using the default failure message on application startup.
        /// </summary>
        /// <typeparam name="TOption">The type of the options.</typeparam>
        /// <typeparam name="TOptionValidator">The fluent validator of the options.</typeparam>
        /// <param name="config">The configuration.</param>
        /// <param name="name">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <param name="configureBinder">Used to configure the binder options.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection Configure<TOption, TOptionValidator>(
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
                    @"The configuration must be provided when the binder is configured."
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
        /// <param name="setupAction">The configuration.</param>
        /// <param name="name">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection Configure<TOption, TOptionValidator>(
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
        /// <param name="setupAction">The configuration.</param>
        /// <param name="name">The name of the options instance.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection Configure<TOption, TOptionValidator>(
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
        /// <param name="config">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <param name="configureBinder">Used to configure the binder options.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
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
        /// <param name="configureOption">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
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
        /// <param name="configureOption">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateDataAnnotation<TOption>(
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
        /// <param name="config">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <param name="configureBinder">Used to configure the binder options.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
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
        /// <param name="configureOption">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
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
        /// <param name="configureOption">The configuration.</param>
        /// <param name="validation">The validation function.</param>
        /// <returns>The same services collection.</returns>
        public IServiceCollection ConfigureWithValidateFluentValidation<TOption>(
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
    }

    extension<TOptions>(OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        #region Helpers
        private OptionsBuilder<TOptions> _ValidateFunc(Func<TOptions, bool>? validation)
        {
            if (validation is not null)
            {
                builder.Validate(validation);
            }

            return builder;
        }

        private OptionsBuilder<TOptions> _AddSetupAction(Action<TOptions, IServiceProvider>? setupAction)
        {
            if (setupAction is not null)
            {
                builder.Configure(setupAction);
            }

            return builder;
        }

        private OptionsBuilder<TOptions> _AddSetupAction(Action<TOptions>? setupAction)
        {
            if (setupAction is not null)
            {
                builder.Configure(setupAction);
            }

            return builder;
        }

        private OptionsBuilder<TOptions> _AddSetupBind(IConfiguration? config, Action<BinderOptions>? configureBinder)
        {
            if (config is not null)
            {
                builder.Bind(config, configureBinder);
            }

            return builder;
        }
        #endregion
    }
}
