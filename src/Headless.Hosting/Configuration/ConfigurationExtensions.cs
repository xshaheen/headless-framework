// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

[PublicAPI]
public static class ConfigurationExtensions
{
    extension(IConfiguration configuration)
    {
        /// <summary>Binds the named configuration section to a new <typeparamref name="TModel"/> instance.</summary>
        /// <typeparam name="TModel">The model type to bind.</typeparam>
        /// <param name="section">The name of the configuration section to bind.</param>
        /// <returns>The bound <typeparamref name="TModel"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null"/>.</exception>
        public TModel GetOptions<TModel>(string section)
            where TModel : new()
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(section);

            var model = new TModel();
            configuration.GetSection(section).Bind(model);

            return model;
        }

        /// <summary>Shorthand for GetSection("ConnectionStrings")[name].</summary>
        /// <param name="name">The connection string key.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the connection string is missing or blank.</exception>
        public string GetRequiredConnectionString(string name)
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(name);

            var connectionString = configuration.GetSection("ConnectionStrings")[name];

            // Treat a present-but-blank value as missing so misconfiguration fails here with a clear
            // message rather than later at the ADO/connection layer.
            return string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException($"Connection string '{name}' not found.")
                : connectionString;
        }

        /// <summary>Binds the whole configuration to <typeparamref name="T"/>, throwing when binding yields null.</summary>
        /// <typeparam name="T">The type to bind to.</typeparam>
        /// <param name="configureOptions">An optional delegate to configure the binder options.</param>
        /// <returns>The bound <typeparamref name="T"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the configuration cannot be bound to <typeparamref name="T"/>.</exception>
        public T GetRequired<T>(Action<BinderOptions>? configureOptions = null)
        {
            Argument.IsNotNull(configuration);

            return configuration.Get<T>(configureOptions)
                ?? throw new InvalidOperationException($"Missing configurations {typeof(T).Name}");
        }

        /// <summary>Binds the named configuration section to <typeparamref name="T"/>, throwing when binding yields null.</summary>
        /// <typeparam name="T">The type to bind to.</typeparam>
        /// <param name="key">The name of the configuration section to bind.</param>
        /// <param name="configureOptions">An optional delegate to configure the binder options.</param>
        /// <returns>The bound <typeparamref name="T"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the section cannot be bound to <typeparamref name="T"/>.</exception>
        public T GetRequired<T>(string key, Action<BinderOptions>? configureOptions = null)
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(key);

            var section = configuration.GetSection(key);

            return section.GetRequired<T>(configureOptions);
        }

        /// <summary>
        /// Binds the named configuration section to <typeparamref name="TOption"/> then validates it with a
        /// freshly constructed <typeparamref name="TValidator"/>.
        /// </summary>
        /// <typeparam name="TOption">The options type to bind.</typeparam>
        /// <typeparam name="TValidator">The validator used to validate the bound option.</typeparam>
        /// <param name="key">The name of the configuration section to bind.</param>
        /// <param name="configureOptions">An optional delegate to configure the binder options.</param>
        /// <returns>The bound and validated <typeparamref name="TOption"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the section is missing or fails validation.</exception>
        public TOption GetRequired<TOption, TValidator>(string key, Action<BinderOptions>? configureOptions = null)
            where TValidator : class, IValidator<TOption>, new()
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(key);

            var section = configuration.GetSection(key);

            return section.GetRequired<TOption, TValidator>(configureOptions);
        }

        /// <summary>
        /// Binds and returns <typeparamref name="TOption"/>, then validates it with a freshly
        /// constructed <typeparamref name="TValidator"/>.
        /// </summary>
        /// <remarks>
        /// The <c>new()</c> constraint is intentional: this helper runs at configuration-read time —
        /// before the service provider exists — so the validator cannot be resolved from DI and must
        /// have a parameterless constructor. For validators with injected dependencies, register the
        /// options through the DI pipeline instead (for example
        /// <c>services.Configure&lt;TOption, TValidator&gt;(config)</c>).
        /// </remarks>
        /// <typeparam name="TOption">The options type to bind.</typeparam>
        /// <typeparam name="TValidator">The validator used to validate the bound option.</typeparam>
        /// <param name="configureOptions">An optional delegate to configure the binder options.</param>
        /// <returns>The bound and validated <typeparamref name="TOption"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the configuration cannot be bound or the bound value fails validation.</exception>
        public TOption GetRequired<TOption, TValidator>(Action<BinderOptions>? configureOptions = null)
            where TValidator : class, IValidator<TOption>, new()
        {
            Argument.IsNotNull(configuration);

            var option = configuration.GetRequired<TOption>(configureOptions);
            var validator = new TValidator();
            var result = validator.Validate(option);

            if (result.IsValid)
            {
                return option;
            }

            var errors = new StringBuilder($"Option {typeof(TOption).Name} has errors");

            errors.AppendLine();

            foreach (var error in result.Errors)
            {
                errors.AppendLine(CultureInfo.InvariantCulture, $"{error.ErrorCode}: {error.ErrorMessage}");
            }

            throw new InvalidOperationException(errors.ToString());
        }
    }
}
