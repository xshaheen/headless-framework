// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Input validation failed. Contains field-level errors.
/// </summary>
[PublicAPI]
public sealed record ValidationError : ResultError
{
    private IReadOnlyDictionary<string, object?>? _metadata;

    public required IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; init; }

    public override string Code => "validation:failed";
    public override string Message => "One or more validation errors occurred.";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        _metadata ??= FieldErrors.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);

    public static ValidationError FromFields(params (string Field, string Error)[] errors)
    {
        var grouped = errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(e => e.Error).ToList(),
                StringComparer.Ordinal
            );

        return new ValidationError { FieldErrors = grouped };
    }
}
