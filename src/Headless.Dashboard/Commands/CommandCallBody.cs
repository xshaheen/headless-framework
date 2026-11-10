// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard;

/// <summary>
/// The JSON body of a command, confirm, or chunk call. Each endpoint reads the fields it needs and refuses the rest of
/// the call's rules itself.
/// </summary>
internal sealed record CommandCallBody
{
    /// <summary>The ids to act on, de-duplicated in their given order; <see langword="null"/> when none were sent.</summary>
    public IReadOnlyList<string>? Ids { get; init; }

    /// <summary>The filter to act over, cloned from the body; <see langword="null"/> when none (or a JSON null) was sent.</summary>
    public JsonElement? Filter { get; init; }

    public JsonElement? Arguments { get; init; }

    public string? Reason { get; init; }

    public bool Confirmed { get; init; }

    public long? TypedCount { get; init; }

    public string? Token { get; init; }

    public string? Cursor { get; init; }

    /// <summary>Reads the request body.</summary>
    /// <returns>The body, or the errors of every field of the wrong JSON type, or of a body that is not a JSON object.</returns>
    public static async Task<(CommandCallBody? Body, IReadOnlyList<CommandFieldError> Errors)> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        JsonDocument document;

        try
        {
            document = await JsonDocument
                .ParseAsync(request.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return (null, [new CommandFieldError("body", "json", "The request body must be a JSON object.")]);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, [new CommandFieldError("body", "json", "The request body must be a JSON object.")]);
            }

            var errors = new List<CommandFieldError>();
            var body = new CommandCallBody
            {
                Ids = _Ids(root, errors),
                Filter = _Value(root, "filter"),
                Arguments = _Value(root, "arguments"),
                Reason = _String(root, "reason", errors),
                Confirmed = _Boolean(root, "confirmed", errors),
                TypedCount = _Integer(root, "typedCount", errors),
                Token = _String(root, "token", errors),
                Cursor = _String(root, "cursor", errors),
            };

            return errors.Count == 0 ? (body, []) : (null, errors);
        }
    }

    private static JsonElement? _Value(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : null;
    }

    private static string? _String(JsonElement root, string name, List<CommandFieldError> errors)
    {
        if (_Value(root, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new CommandFieldError(name, "type", $"'{name}' must be a string."));
            return null;
        }

        return value.GetString();
    }

    private static bool _Boolean(JsonElement root, string name, List<CommandFieldError> errors)
    {
        if (_Value(root, name) is not { } value)
        {
            return false;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(new CommandFieldError(name, "type", $"'{name}' must be a boolean."));
            return false;
        }

        return value.GetBoolean();
    }

    private static long? _Integer(JsonElement root, string name, List<CommandFieldError> errors)
    {
        if (_Value(root, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            errors.Add(new CommandFieldError(name, "type", $"'{name}' must be an integer."));
            return null;
        }

        return number;
    }

    private static List<string>? _Ids(JsonElement root, List<CommandFieldError> errors)
    {
        if (_Value(root, "ids") is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new CommandFieldError("ids", "type", "'ids' must be an array of strings."));
            return null;
        }

        var ids = new List<string>(value.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                errors.Add(new CommandFieldError("ids", "type", "'ids' must be an array of non-empty strings."));
                return null;
            }

            var id = item.GetString()!;

            // A repeated id is one row: counting it twice would inflate the count the operator confirms.
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
