// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

[PublicAPI]
public static class ConfigurationExtensions
{
    extension(IConfiguration configuration)
    {
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
        public string GetRequiredConnectionString(string name)
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(name);

            return configuration.GetSection("ConnectionStrings")[name]
                ?? throw new InvalidOperationException($"Connection string '{name}' not found.");
        }

        public T GetRequired<T>(Action<BinderOptions>? configureOptions = null)
        {
            Argument.IsNotNull(configuration);

            return configuration.Get<T>(configureOptions)
                ?? throw new InvalidOperationException($"Missing configurations {typeof(T).Name}");
        }

        public T GetRequired<T>(string key, Action<BinderOptions>? configureOptions = null)
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(key);

            var section = configuration.GetSection(key);

            return section.GetRequired<T>(configureOptions);
        }

        public TOption GetRequired<TOption, TValidator>(string key, Action<BinderOptions>? configureOptions = null)
            where TValidator : class, IValidator<TOption>, new()
        {
            Argument.IsNotNull(configuration);
            Argument.IsNotNull(key);

            var section = configuration.GetSection(key);

            return section.GetRequired<TOption, TValidator>(configureOptions);
        }

        public TOption GetRequired<TOption, TValidator>(Action<BinderOptions>? configureOptions = null)
            where TValidator : class, IValidator<TOption>, new()
        {
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
