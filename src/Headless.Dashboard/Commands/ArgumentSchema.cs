// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A command's arguments as the JSON-Schema subset the shell renders as a form: an object whose properties carry
/// <c>"type"</c>, <c>"enum"</c>, <c>"minimum"</c>, <c>"maximum"</c>, and <c>"maxLength"</c>, with the <c>"required"</c> list.
/// </summary>
internal sealed record ArgumentSchema
{
    /// <summary>Always <c>"object"</c>: the arguments travel as one JSON object keyed by argument name.</summary>
    public string Type { get; } = "object";

    public required IReadOnlyList<string> Required { get; init; }

    /// <summary>The arguments by name, in the order the command declares them.</summary>
    public required IReadOnlyDictionary<string, ArgumentPropertySchema> Properties { get; init; }

    /// <summary>Builds the schema of <paramref name="arguments"/>.</summary>
    public static ArgumentSchema Create(IReadOnlyList<DashboardCommandArgument> arguments)
    {
        // A list-backed dictionary keeps the declared order, which is the form's field order.
        var properties = new OrderedDictionary<string, ArgumentPropertySchema>(arguments.Count, StringComparer.Ordinal);

        foreach (var argument in arguments)
        {
            properties[argument.Name] = new ArgumentPropertySchema
            {
                Type = TypeName(argument.Kind),
                Title = argument.Label,
                Enum = argument.AllowedValues?.Select(value => _EnumValue(argument.Kind, value)).ToArray(),
                Minimum = _IsNumber(argument.Kind) ? argument.Minimum : null,
                Maximum = _IsNumber(argument.Kind) ? argument.Maximum : null,
                MaxLength = argument.Kind == DashboardArgumentKind.Text ? argument.MaxLength : null,
            };
        }

        return new ArgumentSchema
        {
            Required = [.. arguments.Where(static a => a.Required).Select(static a => a.Name)],
            Properties = properties,
        };
    }

    /// <summary>The JSON-Schema <c>"type"</c> of an argument kind.</summary>
    public static string TypeName(DashboardArgumentKind kind)
    {
        return kind switch
        {
            DashboardArgumentKind.WholeNumber => "integer",
            DashboardArgumentKind.Number => "number",
            DashboardArgumentKind.Toggle => "boolean",
            _ => "string",
        };
    }

    private static bool _IsNumber(DashboardArgumentKind kind)
    {
        return kind is DashboardArgumentKind.WholeNumber or DashboardArgumentKind.Number;
    }

    // Allowed values are declared as text; the schema states them as values of the argument's own type, so the form
    // compares like with like. A value that does not parse as its kind stays text, and the server then refuses every
    // input for it, which surfaces the module's mistake instead of hiding it.
    private static JsonElement _EnumValue(DashboardArgumentKind kind, string value)
    {
        return kind switch
        {
            DashboardArgumentKind.WholeNumber
            or DashboardArgumentKind.Number
                when decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) =>
                JsonSerializer.SerializeToElement(number, ArgumentSchemaJsonContext.Default.Decimal),
            DashboardArgumentKind.Toggle when bool.TryParse(value, out var toggle) => JsonSerializer.SerializeToElement(
                toggle,
                ArgumentSchemaJsonContext.Default.Boolean
            ),
            _ => JsonSerializer.SerializeToElement(value, ArgumentSchemaJsonContext.Default.String),
        };
    }
}

/// <summary>One argument in an <see cref="ArgumentSchema"/>.</summary>
internal sealed record ArgumentPropertySchema
{
    /// <summary><c>"string"</c>, <c>"integer"</c>, <c>"number"</c>, or <c>"boolean"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The form label.</summary>
    public required string Title { get; init; }

    public IReadOnlyList<JsonElement>? Enum { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public int? MaxLength { get; init; }
}

[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
internal sealed partial class ArgumentSchemaJsonContext : JsonSerializerContext;
