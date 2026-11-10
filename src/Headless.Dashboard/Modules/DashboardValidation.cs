// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using FluentValidation.Results;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Dashboard;

/// <summary>
/// The one shape every dashboard endpoint refuses input in: a <c>400</c> validation problem naming each refused field
/// with its messages, and a <c>rules</c> extension naming the rule each field broke. Commands answer it, and a module's
/// own endpoints answer it through these methods, so the SPA reads every refusal the same way.
/// </summary>
/// <remarks>
/// Validate a request body or query with a FluentValidation validator and pass its result. Keep a refusal that depends
/// on stored state, such as a run that is no longer cancellable, out of the validator: it belongs to the command's
/// rules, which re-check it when the command applies.
/// </remarks>
public static class DashboardValidation
{
    /// <summary>The rule a field breaks when it is missing or empty.</summary>
    public const string RequiredRule = "required";

    /// <summary>The rule a field breaks when its value is not acceptable.</summary>
    public const string InvalidRule = "invalid";

    /// <summary>The rule a field breaks when its value is not one of the accepted names.</summary>
    public const string EnumRule = "enum";

    /// <summary>The rule a field breaks when its value is below its floor.</summary>
    public const string MinimumRule = "minimum";

    /// <summary>The rule a field breaks when its value is above its ceiling.</summary>
    public const string MaximumRule = "maximum";

    /// <summary>The rule a field breaks when its text is longer than its limit.</summary>
    public const string MaxLengthRule = "maxLength";

    /// <summary>The rule a field breaks when its text does not parse.</summary>
    public const string FormatRule = "format";

    /// <summary>The rule <see cref="BodyField"/> breaks when the body is not the JSON object the call takes.</summary>
    public const string JsonRule = "json";

    /// <summary>The field a refusal names when the body as a whole is wrong, such as malformed JSON.</summary>
    public const string BodyField = "body";

    /// <summary>
    /// Answers the failures of <paramref name="result"/>. Each field is the failure's property path in camel case
    /// (<c>children[0].function</c>); its rule is the failure's error code when the validator set one with
    /// <c>WithErrorCode</c>, <see cref="RequiredRule"/> for an empty or missing value, and <see cref="InvalidRule"/>
    /// otherwise.
    /// </summary>
    public static ValidationProblem Problem(ValidationResult result, string? detail = null)
    {
        Argument.IsNotNull(result);

        var errors = result
            .Errors.Select(static failure => new CommandFieldError(
                _FieldOf(failure.PropertyName),
                _RuleOf(failure.ErrorCode),
                failure.ErrorMessage
            ))
            .ToArray();

        return Problem(errors, detail ?? errors.FirstOrDefault()?.Message);
    }

    /// <summary>Answers one refused <paramref name="field"/>; its message is also the problem's detail.</summary>
    public static ValidationProblem Problem(string field, string rule, string message)
    {
        Argument.IsNotNull(field);
        Argument.IsNotNullOrWhiteSpace(rule);
        Argument.IsNotNullOrWhiteSpace(message);

        return Problem([new CommandFieldError(field, rule, message)], message);
    }

    /// <summary>
    /// The member of <typeparamref name="TEnum"/> that <paramref name="value"/> names, case-insensitive, or
    /// <see langword="null"/> when it is empty or names none. A number counts only when the enum declares it, so a query
    /// never reaches a value no member names.
    /// </summary>
    internal static TEnum? ParseEnum<TEnum>(string? value)
        where TEnum : struct, Enum =>
        !string.IsNullOrEmpty(value)
        && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;

    internal static ValidationProblem Problem(IReadOnlyCollection<CommandFieldError> errors, string? detail)
    {
        var messages = errors
            .GroupBy(static error => error.Field, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static error => error.Message).ToArray(),
                StringComparer.Ordinal
            );
        var rules = errors
            .GroupBy(static error => error.Field, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Rule, StringComparer.Ordinal);

        return TypedResults.ValidationProblem(
            messages,
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["rules"] = rules }
        );
    }

    // FluentValidation names its built-in codes after the validator class; a dashboard rule is a short lower-case word.
    private static string _RuleOf(string? errorCode) =>
        errorCode switch
        {
            null or "" => InvalidRule,
            "NotEmptyValidator" or "NotNullValidator" => RequiredRule,
            _ when errorCode.EndsWith("Validator", StringComparison.Ordinal) => InvalidRule,
            _ => errorCode,
        };

    // The SPA names fields as the JSON body does, so each segment of the property path is camel-cased.
    private static string _FieldOf(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return string.Empty;
        }

        return string.Join(
            '.',
            propertyName.Split('.').Select(static segment => JsonNamingPolicy.CamelCase.ConvertName(segment))
        );
    }
}
