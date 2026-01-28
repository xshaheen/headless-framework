// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Options;

public static class OptionsBuilderFluentValidationExtensions
{
    /// <summary>Register this options instance for validation of its fluent validation.</summary>
    /// <typeparam name="TOptions">The options type to be configured.</typeparam>
    /// <param name="optionsBuilder">The options builder to add the services to.</param>
    /// <returns>The <see cref="OptionsBuilder{TOptions}"/> so that additional calls can be chained.</returns>
    public static OptionsBuilder<TOptions> ValidateFluentValidation<TOptions>(
        this OptionsBuilder<TOptions> optionsBuilder
    )
        where TOptions : class
    {
        Argument.IsNotNull(optionsBuilder);

        optionsBuilder.Services.AddTransient<IValidateOptions<TOptions>>(
            provider => new FluentValidationValidateOptions<TOptions>(optionsBuilder.Name, provider)
        );

        return optionsBuilder;
    }

    private sealed class FluentValidationValidateOptions<TOptions>(string? optionName, IServiceProvider serviceProvider)
        : IValidateOptions<TOptions>
        where TOptions : class
    {
        /// <summary>
        /// Validates a specific named options instance (or all when <paramref name="name"/> is null).
        /// </summary>
        /// <param name="name">The name of the options instance being validated.</param>
        /// <param name="options">The options instance.</param>
        /// <returns>The <see cref="ValidateOptionsResult"/> result.</returns>
        public ValidateOptionsResult Validate(string? name, TOptions options)
        {
            // Null name is used to configure all named options.
            if (optionName is not null && !string.Equals(optionName, name, StringComparison.Ordinal))
            {
                // Ignored if not validating this instance.
                return ValidateOptionsResult.Skip;
            }

            // Ensure options are provided to validate against
            Argument.IsNotNull(options);

            var builder = new ValidateOptionsResultBuilder();

            using var scope = serviceProvider.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<IValidator<TOptions>>();

            var validationResult = validator.Validate(options);

            if (!validationResult.IsValid)
            {
                var optionTypeName = typeof(TOptions).Name;

                foreach (var error in validationResult.Errors)
                {
                    builder.AddError(error.ErrorMessage, $"{optionTypeName}.{error.PropertyName}");
                }
            }

            return builder.Build();
        }
    }
}
