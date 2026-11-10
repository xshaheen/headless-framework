// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Dashboard;

/// <summary>
/// Validates a command call's arguments against the command's declared schema on the server, so a module receives only
/// arguments of the declared kind, within the declared bounds, with every required one present and no undeclared one.
/// </summary>
internal static partial class ArgumentSchemaValidator
{
    /// <summary>The field prefix every argument error carries, so <c>count</c> reports as <c>arguments.count</c>.</summary>
    public const string FieldPrefix = "arguments.";

    /// <summary>
    /// Validates <paramref name="arguments"/>, the call's <c>arguments</c> value, against <paramref name="schema"/>.
    /// </summary>
    /// <param name="schema">The command's declared arguments.</param>
    /// <param name="arguments">The call's arguments object, or <see langword="null"/> when the call sent none.</param>
    /// <returns>
    /// The validated values, cloned so they outlive the request body, or one error per invalid field naming the rule
    /// it broke: <c>"type"</c>, <c>"required"</c>, <c>"enum"</c>, <c>"minimum"</c>, <c>"maximum"</c>, <c>"maxLength"</c>,
    /// <c>"format"</c>, or <c>"additionalProperties"</c>.
    /// </returns>
    /// <param name="now">The moment a <see cref="DashboardCommandArgument.NotBeforeNow"/> instant is checked against.</param>
    public static ArgumentValidationResult Validate(
        IReadOnlyList<DashboardCommandArgument> schema,
        JsonElement? arguments,
        DateTimeOffset now
    )
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var errors = new List<CommandFieldError>();

        if (arguments is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given)
        {
            if (given.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new CommandFieldError("arguments", "type", "Arguments must be a JSON object."));
                return new ArgumentValidationResult(values, errors);
            }

            foreach (var property in given.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    values[property.Name] = property.Value.Clone();
                }
            }
        }

        foreach (var name in values.Keys)
        {
            if (!schema.Any(argument => string.Equals(argument.Name, name, StringComparison.Ordinal)))
            {
                errors.Add(
                    new CommandFieldError(FieldPrefix + name, "additionalProperties", $"'{name}' is not an argument.")
                );
            }
        }

        foreach (var argument in schema)
        {
            if (!values.TryGetValue(argument.Name, out var value))
            {
                if (argument.Required)
                {
                    errors.Add(
                        new CommandFieldError(
                            FieldPrefix + argument.Name,
                            "required",
                            $"'{argument.Name}' is required."
                        )
                    );
                }

                continue;
            }

            if (_Check(argument, value, now) is { } error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            values.Clear();
        }

        return new ArgumentValidationResult(values, errors);
    }

    private static CommandFieldError? _Check(DashboardCommandArgument argument, JsonElement value, DateTimeOffset now)
    {
        var field = FieldPrefix + argument.Name;

        if (!_HasKind(argument.Kind, value))
        {
            return new CommandFieldError(
                field,
                "type",
                $"'{argument.Name}' must be of type {ArgumentSchema.TypeName(argument.Kind)}."
            );
        }

        if (
            argument.AllowedValues is { } allowed
            && !allowed.Any(candidate => _Equals(argument.Kind, value, candidate))
        )
        {
            return new CommandFieldError(
                field,
                "enum",
                $"'{argument.Name}' must be one of: {string.Join(", ", allowed)}."
            );
        }

        if (argument.Kind is DashboardArgumentKind.WholeNumber or DashboardArgumentKind.Number)
        {
            var number = value.GetDouble();

            if (argument.Minimum is { } minimum && number < minimum)
            {
                return new CommandFieldError(
                    field,
                    "minimum",
                    $"'{argument.Name}' must be at least {minimum.ToString(CultureInfo.InvariantCulture)}."
                );
            }

            if (argument.Maximum is { } maximum && number > maximum)
            {
                return new CommandFieldError(
                    field,
                    "maximum",
                    $"'{argument.Name}' must be at most {maximum.ToString(CultureInfo.InvariantCulture)}."
                );
            }
        }

        if (argument.Kind == DashboardArgumentKind.DateTime)
        {
            // The client always sends an offset; text without one would be read in the server's own zone, which is
            // the wrong instant for every operator not sitting in it.
            if (!value.TryGetDateTimeOffset(out var instant) || !_OffsetPattern.IsMatch(value.GetString()!))
            {
                return new CommandFieldError(
                    field,
                    "format",
                    $"'{argument.Name}' must be an ISO 8601 date and time with an offset, such as 2026-12-15T08:00:00Z."
                );
            }

            if (argument.NotBeforeNow && instant < now)
            {
                return new CommandFieldError(field, "minimum", $"'{argument.Name}' must not be in the past.");
            }
        }

        // JSON Schema counts characters, not UTF-16 units, so an emoji counts once.
        if (
            argument is { Kind: DashboardArgumentKind.Text or DashboardArgumentKind.Json, MaxLength: { } maxLength }
            && value.GetString()!.EnumerateRunes().Take(maxLength + 1).Count() > maxLength
        )
        {
            return new CommandFieldError(
                field,
                "maxLength",
                $"'{argument.Name}' must be at most {maxLength.ToString(CultureInfo.InvariantCulture)} characters."
            );
        }

        // A form can be bypassed, so the text is parsed here even though the client checked it.
        if (argument.Kind == DashboardArgumentKind.Json && !_IsJson(value.GetString()!))
        {
            return new CommandFieldError(field, "format", $"'{argument.Name}' must be valid JSON.");
        }

        return null;
    }

    // `TryGetDateTimeOffset` reads text without an offset as server-local time instead of refusing it, so this asks
    // whether the text, already proved ISO 8601, ends in an offset after its time.
    [GeneratedRegex(@"T.*(?:[Zz]|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex _OffsetPattern { get; }

    private static bool _IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool _HasKind(DashboardArgumentKind kind, JsonElement value)
    {
        return kind switch
        {
            DashboardArgumentKind.Text => value.ValueKind == JsonValueKind.String,
            DashboardArgumentKind.WholeNumber => value.ValueKind == JsonValueKind.Number
                && value.TryGetDecimal(out var whole)
                && decimal.Truncate(whole) == whole,
            DashboardArgumentKind.Number => value.ValueKind == JsonValueKind.Number,
            DashboardArgumentKind.Toggle => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            DashboardArgumentKind.DateTime or DashboardArgumentKind.Json => value.ValueKind == JsonValueKind.String,
            _ => false,
        };
    }

    private static bool _Equals(DashboardArgumentKind kind, JsonElement value, string candidate)
    {
        return kind switch
        {
            DashboardArgumentKind.Text or DashboardArgumentKind.DateTime or DashboardArgumentKind.Json => string.Equals(
                value.GetString(),
                candidate,
                StringComparison.Ordinal
            ),
            DashboardArgumentKind.Toggle => bool.TryParse(candidate, out var toggle)
                && toggle == (value.ValueKind == JsonValueKind.True),
            _ => value.TryGetDecimal(out var number)
                && decimal.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var allowed)
                && number == allowed,
        };
    }
}

/// <summary>The outcome of validating a call's arguments.</summary>
/// <param name="Values">The validated arguments by name; empty when any is invalid.</param>
/// <param name="Errors">One error per invalid field; empty when the arguments are valid.</param>
internal sealed record ArgumentValidationResult(
    IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyList<CommandFieldError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>One refused field of a command call: its name, the rule it broke, and a message for the operator.</summary>
/// <param name="Field">The field, such as <c>reason</c> or <c>arguments.count</c>.</param>
/// <param name="Rule">The rule, such as <c>"required"</c> or <c>"maximum"</c>.</param>
/// <param name="Message">What to change.</param>
internal sealed record CommandFieldError(string Field, string Rule, string Message);
