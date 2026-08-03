// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Base class for all result errors. Extend this to create domain-specific errors.
/// </summary>
[PublicAPI]
public abstract record ApiResultError
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
    /// <returns>A <see cref="ApiResultError"/> carrying the supplied code and message.</returns>
    public static ApiResultError Custom(string code, string message)
    {
        return new SimpleError(code, message);
    }

    private sealed record SimpleError(string Code, string Message) : ApiResultError
    {
        public override string Code { get; } = Code;
        public override string Message { get; } = Message;
    }
}

/// <summary>
/// Multiple errors occurred. Useful for batch operations.
/// </summary>
[PublicAPI]
public sealed record AggregateError : ApiResultError
{
    /// <summary>The individual errors that were aggregated.</summary>
    public required IReadOnlyList<ApiResultError> Errors
    {
        get;
        init => field = _CopyErrors(value);
    }

    /// <inheritdoc/>
    public override string Code => "aggregate:multiple_errors";

    /// <inheritdoc/>
    public override string Message => $"{Errors.Count} errors occurred.";

    /// <summary>Determines equality from the ordered contained errors.</summary>
    /// <param name="other">The aggregate error to compare with.</param>
    /// <returns><see langword="true"/> when both aggregates contain equal errors in the same order.</returns>
    public bool Equals(AggregateError? other) => other is not null && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var error in Errors)
        {
            hash.Add(error);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Tries to merge this aggregate when every contained error is a validation error.
    /// </summary>
    /// <param name="errors">The merged field-keyed validation descriptors when successful.</param>
    /// <returns><see langword="true"/> when every contained error is a validation error.</returns>
    public bool TryGetValidationErrors(
        [NotNullWhen(true)] out IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>? errors
    )
    {
        var merged = new Dictionary<string, List<ErrorDescriptor>>(StringComparer.Ordinal);

        foreach (var error in Errors)
        {
            if (!_TryAppendValidationErrors(error, merged))
            {
                errors = null;
                return false;
            }
        }

        errors = merged.ToDictionary(
            pair => pair.Key,
            IReadOnlyList<ErrorDescriptor> (pair) => pair.Value,
            StringComparer.Ordinal
        );
        return true;
    }

    /// <summary>Flattens the aggregate into structured descriptors for conflict responses.</summary>
    /// <returns>The descriptors carried by all nested errors.</returns>
    public IReadOnlyList<ErrorDescriptor> ToErrorDescriptors()
    {
        var descriptors = new List<ErrorDescriptor>();

        foreach (var error in Errors)
        {
            _AppendErrorDescriptors(error, descriptors);
        }

        return descriptors;
    }

    private static IReadOnlyList<ApiResultError> _CopyErrors(IReadOnlyList<ApiResultError> errors)
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);
        Argument.HasNoNulls<ApiResultError>(checkedErrors);
        return [.. checkedErrors];
    }

    private static bool _TryAppendValidationErrors(
        ApiResultError error,
        Dictionary<string, List<ErrorDescriptor>> merged
    )
    {
        if (error is AggregateError aggregate)
        {
            return aggregate.Errors.All(item => _TryAppendValidationErrors(item, merged));
        }

        if (error is not ValidationError validation)
        {
            return false;
        }

        foreach (var (field, descriptors) in validation.Errors)
        {
            if (!merged.TryGetValue(field, out var fieldErrors))
            {
                fieldErrors = [];
                merged[field] = fieldErrors;
            }

            fieldErrors.AddRange(descriptors);
        }

        return true;
    }

    private static void _AppendErrorDescriptors(ApiResultError error, List<ErrorDescriptor> descriptors)
    {
        switch (error)
        {
            case AggregateError aggregate:
                foreach (var nested in aggregate.Errors)
                {
                    _AppendErrorDescriptors(nested, descriptors);
                }

                break;
            case ConflictError conflict:
                descriptors.AddRange(conflict.Errors);
                break;
            case ForbiddenError forbidden:
                descriptors.Add(forbidden.Error);
                break;
            case UnauthorizedError { Error: not null } unauthorized:
                descriptors.Add(unauthorized.Error);
                break;
            case ValidationError validation:
                foreach (var validationErrors in validation.Errors.Values)
                {
                    descriptors.AddRange(validationErrors);
                }

                break;
            default:
                descriptors.Add(new ErrorDescriptor(error.Code, error.Message));
                break;
        }
    }
}

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public sealed record NotFoundError : ApiResultError
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
public sealed record UnauthorizedError : ApiResultError
{
    /// <summary>
    /// Cached singleton instance for common case.
    /// </summary>
    public static readonly UnauthorizedError Instance = new();

    /// <summary>Initializes the generic unauthorized error without a public descriptor.</summary>
    public UnauthorizedError() { }

    /// <summary>Initializes an unauthorized error carrying a structured descriptor.</summary>
    /// <param name="error">The descriptor exposed in the 401 ProblemDetails response.</param>
    public UnauthorizedError(ErrorDescriptor error)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The optional descriptor exposed in the 401 ProblemDetails response.</summary>
    public ErrorDescriptor? Error { get; }

    /// <inheritdoc/>
    public override string Code => Error?.Code ?? "unauthorized";

    /// <inheritdoc/>
    public override string Message => Error?.Description ?? "Authentication required.";

    /// <summary>Determines equality from descriptor presence plus the public code and message.</summary>
    /// <param name="other">The unauthorized error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry the same code and message.</returns>
    public bool Equals(UnauthorizedError? other) =>
        other is not null
        && (Error is null) == (other.Error is null)
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Error is not null, Code, Message);
}

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public sealed record ForbiddenError : ApiResultError
{
    /// <summary>Initializes a forbidden error carrying a structured descriptor.</summary>
    /// <param name="error">The descriptor exposed in the 403 ProblemDetails response.</param>
    public ForbiddenError(ErrorDescriptor error)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The descriptor exposed in the 403 ProblemDetails response.</summary>
    public ErrorDescriptor Error { get; }

    /// <summary>The reason the operation is not permitted.</summary>
    public string Reason => Error.Description;

    /// <inheritdoc/>
    public override string Code => Error.Code;

    /// <inheritdoc/>
    public override string Message => Error.Description;

    /// <summary>Determines equality from the public code and message, independent of descriptor identity.</summary>
    /// <param name="other">The forbidden error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry the same code and message.</returns>
    public bool Equals(ForbiddenError? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Message);
}

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
[PublicAPI]
public sealed record ConflictError : ApiResultError
{
    /// <summary>Initializes a conflict from a code and message.</summary>
    /// <param name="code">A machine-readable code describing the conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    public ConflictError(string code, string message)
        : this(new ErrorDescriptor(code, message)) { }

    /// <summary>Initializes a conflict from one descriptor.</summary>
    /// <param name="error">The descriptor describing the conflict.</param>
    public ConflictError(ErrorDescriptor error)
    {
        Errors = [Argument.IsNotNull(error)];
    }

    /// <summary>Initializes a conflict from one or more descriptors.</summary>
    /// <param name="errors">The descriptors describing all conflicting conditions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains a <see langword="null"/> item.</exception>
    public ConflictError(params IReadOnlyCollection<ErrorDescriptor> errors)
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);
        Argument.HasNoNulls<ErrorDescriptor>(checkedErrors);
        Errors = [.. checkedErrors];
    }

    /// <summary>The descriptors describing all conflicting conditions.</summary>
    public IReadOnlyList<ErrorDescriptor> Errors { get; }

    /// <inheritdoc/>
    public override string Code => Errors.Count == 1 ? Errors[0].Code : "conflict:multiple_errors";

    /// <inheritdoc/>
    public override string Message => Errors.Count == 1 ? Errors[0].Description : $"{Errors.Count} conflicts occurred.";

    /// <summary>Determines equality from the ordered descriptor codes and messages.</summary>
    /// <param name="other">The conflict error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry equivalent descriptors in the same order.</returns>
    public bool Equals(ConflictError? other) =>
        other is not null && ApiResultErrorEquality.SequenceEquals(Errors, other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode() => ApiResultErrorEquality.GetSequenceHashCode(Errors);
}

/// <summary>
/// Input validation failed. Contains field-level errors.
/// </summary>
[PublicAPI]
public sealed record ValidationError : ApiResultError
{
    /// <summary>The field-level descriptors, keyed by field name.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> Errors
    {
        get;
        init => field = _CopyErrors(value);
    }

    /// <summary>The field-level messages, keyed by field name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors =>
        Errors.ToDictionary(
            pair => pair.Key,
            IReadOnlyList<string> (pair) => pair.Value.Select(error => error.Description).ToList(),
            StringComparer.Ordinal
        );

    /// <inheritdoc/>
    public override string Code => "validation:failed";

    /// <inheritdoc/>
    public override string Message => "One or more validation errors occurred.";

    /// <inheritdoc/>
    // Computed (not field-backed): a `field` backing store would participate in the record's
    // auto-generated equality, so reading it would flip two logically-equal errors to unequal and
    // change GetHashCode mid-lifetime. Build a fresh dictionary on each (cold) read instead.
    public override IReadOnlyDictionary<string, object?> Metadata =>
        Errors.ToDictionary(
            pair => pair.Key,
            object? (pair) => pair.Value.Select(error => error.Description).ToList(),
            StringComparer.Ordinal
        );

    /// <summary>Builds a <see cref="ValidationError"/> from field/error pairs, grouping repeated fields together.</summary>
    /// <param name="errors">The field-error pairs representing the validation issues.</param>
    /// <returns>A <see cref="ValidationError"/> whose <see cref="FieldErrors"/> groups messages by field.</returns>
    public static ValidationError FromFields(params (string Field, string Error)[] errors)
    {
        var grouped = Argument
            .IsNotNullOrEmpty(errors)
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<ErrorDescriptor> (g) =>
                    g.Select(e => new ErrorDescriptor(ApiResultErrorCodes.ValidationFailed, e.Error)).ToList(),
                StringComparer.Ordinal
            );

        return new ValidationError { Errors = grouped };
    }

    /// <summary>Builds a validation error from an already-structured field map.</summary>
    /// <param name="errors">The field-keyed error descriptors.</param>
    /// <returns>A validation error containing a defensive copy of the map and each descriptor list.</returns>
    public static ValidationError FromErrorDescriptors(
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors
    )
    {
        return new ValidationError { Errors = errors };
    }

    /// <summary>
    /// Converts the <see cref="FieldErrors"/> into a dictionary of <see cref="ErrorDescriptor"/> lists keyed by field name.
    /// </summary>
    /// <returns>A dictionary mapping each field name to its list of <see cref="ErrorDescriptor"/> entries.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> ToErrorDescriptorDictionary()
    {
        return Errors;
    }

    /// <summary>Determines equality from field names and ordered descriptor codes and messages.</summary>
    /// <param name="other">The validation error to compare with.</param>
    /// <returns><see langword="true"/> when both errors contain equivalent field errors.</returns>
    public bool Equals(ValidationError? other) =>
        other is not null && ApiResultErrorEquality.DictionaryEquals(Errors, other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode() => ApiResultErrorEquality.GetDictionaryHashCode(Errors);

    private static Dictionary<string, IReadOnlyList<ErrorDescriptor>> _CopyErrors(
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors
    )
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);

        return checkedErrors.ToDictionary(
            pair => pair.Key,
            IReadOnlyList<ErrorDescriptor> (pair) => _CopyFieldErrors(pair.Value),
            StringComparer.Ordinal
        );
    }

    private static IReadOnlyList<ErrorDescriptor> _CopyFieldErrors(IReadOnlyList<ErrorDescriptor> errors)
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);
        Argument.HasNoNulls<ErrorDescriptor>(checkedErrors);
        return [.. checkedErrors];
    }
}

file static class ApiResultErrorEquality
{
    public static bool SequenceEquals(IReadOnlyList<ErrorDescriptor> left, IReadOnlyList<ErrorDescriptor> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => _DescriptorEquals(pair.First, pair.Second));
    }

    public static int GetSequenceHashCode(IReadOnlyList<ErrorDescriptor> errors)
    {
        var hash = new HashCode();

        foreach (var error in errors)
        {
            hash.Add(error.Code, StringComparer.Ordinal);
            hash.Add(error.Description, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool DictionaryEquals(
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> left,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> right
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (field, descriptors) in left)
        {
            if (!right.TryGetValue(field, out var otherDescriptors) || !SequenceEquals(descriptors, otherDescriptors))
            {
                return false;
            }
        }

        return true;
    }

    public static int GetDictionaryHashCode(IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors)
    {
        var hash = new HashCode();

        foreach (var (field, descriptors) in errors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(field, StringComparer.Ordinal);
            hash.Add(GetSequenceHashCode(descriptors));
        }

        return hash.ToHashCode();
    }

    private static bool _DescriptorEquals(ErrorDescriptor left, ErrorDescriptor right)
    {
        return string.Equals(left.Code, right.Code, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal);
    }
}
