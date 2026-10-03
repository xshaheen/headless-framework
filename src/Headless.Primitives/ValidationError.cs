// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

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
        Argument.HasNoNulls(checkedErrors);
        return [.. checkedErrors];
    }
}
