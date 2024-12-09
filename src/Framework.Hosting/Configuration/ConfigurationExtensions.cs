// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

[PublicAPI]
public static class ConfigurationExtensions
{
    public static TModel GetOptions<TModel>(this IConfiguration configuration, string section)
        where TModel : new()
    {
        var model = new TModel();
        configuration.GetSection(section).Bind(model);

        return model;
    }

    /// <summary>
    /// Shorthand for GetSection("ConnectionStrings")[name].
    /// </summary>
    /// <param name="configuration">The configuration to enumerate.</param>
    /// <param name="name">The connection string key.</param>
    /// <returns>The connection string.</returns>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name)
    {
        return configuration.GetSection("ConnectionStrings")[name]
            ?? throw new InvalidOperationException($"Connection string '{name}' not found.");
    }

    public static T GetRequired<T>(this IConfiguration configuration, Action<BinderOptions>? configureOptions = null)
    {
        return configuration.Get<T>(configureOptions)
            ?? throw new InvalidOperationException($"Missing configurations {typeof(T).Name}");
    }

    public static T GetRequired<T>(
        this IConfiguration configuration,
        string key,
        Action<BinderOptions>? configureOptions = null
    )
    {
        var section = configuration.GetSection(key);

        return GetRequired<T>(section, configureOptions);
    }

    public static TOption GetRequired<TOption, TValidator>(
        this IConfiguration configuration,
        string key,
        Action<BinderOptions>? configureOptions = null
    )
        where TValidator : class, IValidator<TOption>, new()
    {
        var section = configuration.GetSection(key);

        return GetRequired<TOption, TValidator>(section, configureOptions);
    }

    public static TOption GetRequired<TOption, TValidator>(
        this IConfiguration configuration,
        Action<BinderOptions>? configureOptions = null
    )
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
