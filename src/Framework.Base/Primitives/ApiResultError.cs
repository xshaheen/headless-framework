// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Base class for all result errors. Extend this to create domain-specific errors.
/// </summary>
[PublicAPI]
public abstract record ResultError
{
    /// <summary>
    /// Machine-readable error code for logging and client handling.
    /// Convention: "namespace:error_name" (e.g., "user:duplicate_email")
    /// </summary>
    public abstract string Code { get; }

    /// <summary>
    /// Human-readable description. Should be localized for end-user display.
    /// </summary>
    public abstract string Message { get; }

    /// <summary>
    /// Additional structured data about the error.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?>? Metadata => null;

    /// <summary>
    /// Creates a simple error without defining a new type.
    /// </summary>
    public static ResultError Custom(string code, string message) => new SimpleError(code, message);

    private sealed record SimpleError(string Code, string Message) : ResultError
    {
        public override string Code { get; } = Code;
        public override string Message { get; } = Message;
    }
}

/// <summary>
/// Multiple errors occurred. Useful for batch operations.
/// </summary>
[PublicAPI]
public sealed record AggregateError : ResultError
{
    public required IReadOnlyList<ResultError> Errors { get; init; }

    public override string Code => "aggregate:multiple_errors";
    public override string Message => $"{Errors.Count} errors occurred.";
}

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public sealed record NotFoundError : ResultError
{
    public required string Entity { get; init; }
    public required string Key { get; init; }

    public override string Code => field ??= $"notfound:{Entity.ToLowerInvariant()}";
    public override string Message => $"{Entity} with key '{Key}' was not found.";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        field ??= new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity"] = Entity, ["key"] = Key };
}

/// <summary>
/// Caller is not authenticated.
/// </summary>
[PublicAPI]
public sealed record UnauthorizedError : ResultError
{
    /// <summary>
    /// Cached singleton instance for common case.
    /// </summary>
    public static readonly UnauthorizedError Instance = new();

    public override string Code => "unauthorized";
    public override string Message => "Authentication required.";
}

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public sealed record ForbiddenError : ResultError
{
    public required string Reason { get; init; }

    public override string Code => "forbidden:access_denied";
    public override string Message => Reason;
}

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
[PublicAPI]
public sealed record ConflictError(string Code, string Message) : ResultError
{
    public override string Code { get; } = Code;
    public override string Message { get; } = Message;
}

/// <summary>
/// Input validation failed. Contains field-level errors.
/// </summary>
[PublicAPI]
public sealed record ValidationError : ResultError
{
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; init; }

    public override string Code => "validation:failed";
    public override string Message => "One or more validation errors occurred.";

    public override IReadOnlyDictionary<string, object?> Metadata
    {
        get => field ??= FieldErrors.ToDictionary(kv => kv.Key, object? (kv) => kv.Value, StringComparer.Ordinal);
    }

    public static ValidationError FromFields(params (string Field, string Error)[] errors)
    {
        var grouped = errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<string> (g) => g.Select(e => e.Error).ToList(),
                StringComparer.Ordinal
            );

        return new ValidationError { FieldErrors = grouped };
    }

    /// <summary>
    /// Converts field errors to a dictionary of ErrorDescriptor lists.
    /// </summary>
    public Dictionary<string, List<ErrorDescriptor>> ToErrorDescriptorDict()
    {
        return FieldErrors.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(msg => new ErrorDescriptor($"validation:{kv.Key}", msg)).ToList(),
            StringComparer.Ordinal
        );
    }
}
