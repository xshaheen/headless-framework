// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

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
    /// Additional structured data about the error, or <see langword="null"/> when none is provided.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?>? Metadata => null;

    /// <summary>
    /// Creates a simple error without defining a new type.
    /// </summary>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <returns>A <see cref="ResultError"/> carrying the supplied code and message.</returns>
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
    /// <summary>The individual errors that were aggregated.</summary>
    public required IReadOnlyList<ResultError> Errors { get; init; }

    /// <inheritdoc/>
    public override string Code => "aggregate:multiple_errors";

    /// <inheritdoc/>
    public override string Message => $"{Errors.Count} errors occurred.";
}

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public sealed record NotFoundError : ResultError
{
    /// <summary>The logical name of the entity that could not be found.</summary>
    public required string Entity { get; init; }

    /// <summary>The key or identifier used to look up the entity.</summary>
    public required string Key { get; init; }

    /// <inheritdoc/>
    // Computed (not field-backed): a `field` backing store would participate in the record's
    // auto-generated equality and flip two logically-equal errors to unequal once read. See ValidationError.Metadata.
    public override string Code => $"notfound:{Entity.ToLowerInvariant()}";

    /// <inheritdoc/>
    public override string Message => $"{Entity} with key '{Key}' was not found.";

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity"] = Entity, ["key"] = Key };
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

    /// <inheritdoc/>
    public override string Code => "unauthorized";

    /// <inheritdoc/>
    public override string Message => "Authentication required.";
}

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public sealed record ForbiddenError : ResultError
{
    /// <summary>The reason the operation is not permitted; also surfaced as the <see cref="Message"/>.</summary>
    public required string Reason { get; init; }

    /// <inheritdoc/>
    public override string Code => "forbidden:access_denied";

    /// <inheritdoc/>
    public override string Message => Reason;
}

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
/// <param name="Code">A machine-readable code describing the type of conflict.</param>
/// <param name="Message">A human-readable message describing the conflict.</param>
[PublicAPI]
public sealed record ConflictError(string Code, string Message) : ResultError
{
    /// <inheritdoc/>
    public override string Code { get; } = Code;

    /// <inheritdoc/>
    public override string Message { get; } = Message;
}

/// <summary>
/// Input validation failed. Contains field-level errors.
/// </summary>
[PublicAPI]
public sealed record ValidationError : ResultError
{
    /// <summary>The field-level errors, keyed by field name, each mapping to one or more error messages.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; init; }

    /// <inheritdoc/>
    public override string Code => "validation:failed";

    /// <inheritdoc/>
    public override string Message => "One or more validation errors occurred.";

    /// <inheritdoc/>
    // Computed (not field-backed): a `field` backing store would participate in the record's
    // auto-generated equality, so reading it would flip two logically-equal errors to unequal and
    // change GetHashCode mid-lifetime. Build a fresh dictionary on each (cold) read instead.
    public override IReadOnlyDictionary<string, object?> Metadata =>
        FieldErrors.ToDictionary(kv => kv.Key, object? (kv) => kv.Value, StringComparer.Ordinal);

    /// <summary>Builds a <see cref="ValidationError"/> from field/error pairs, grouping repeated fields together.</summary>
    /// <param name="errors">The field-error pairs representing the validation issues.</param>
    /// <returns>A <see cref="ValidationError"/> whose <see cref="FieldErrors"/> groups messages by field.</returns>
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
    /// Converts the <see cref="FieldErrors"/> into a dictionary of <see cref="ErrorDescriptor"/> lists keyed by field name.
    /// </summary>
    /// <returns>A dictionary mapping each field name to its list of <see cref="ErrorDescriptor"/> entries.</returns>
    public Dictionary<string, List<ErrorDescriptor>> ToErrorDescriptorDictionary()
    {
        return FieldErrors.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(msg => new ErrorDescriptor($"validation:{kv.Key}", msg)).ToList(),
            StringComparer.Ordinal
        );
    }
}
