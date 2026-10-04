// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

// ReSharper disable once MemberCanBePrivate.Global
/// <summary>
/// Result of validating a domain primitive value.
/// </summary>
public readonly struct PrimitiveValidationResult : IEquatable<PrimitiveValidationResult>
{
    private PrimitiveValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets a value indicating whether the validation succeeded.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsValid { get; }

    /// <summary>
    /// Gets the error message when validation fails, or <see langword="null"/> when validation succeeds.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Represents a successful validation result.
    /// </summary>
    public static readonly PrimitiveValidationResult Ok = new(isValid: true, errorMessage: null);

    /// <summary>
    /// Creates a failed validation result with an error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed validation result.</returns>
    public static PrimitiveValidationResult Error(string error)
    {
        return new(isValid: false, error);
    }

    /// <summary>
    /// Implicitly converts an error string to a failed validation result.
    /// </summary>
    /// <param name="value">The error message.</param>
    /// <returns>A failed validation result.</returns>
    public static implicit operator PrimitiveValidationResult(string value) => Error(value);

    /// <summary>Creates a failed validation result from an error message.</summary>
    public static PrimitiveValidationResult FromString(string value)
    {
        return Error(value);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is PrimitiveValidationResult result && Equals(result);
    }

    /// <inheritdoc/>
    public bool Equals(PrimitiveValidationResult other)
    {
        return IsValid == other.IsValid && string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(IsValid, ErrorMessage);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(PrimitiveValidationResult left, PrimitiveValidationResult right)
    {
        return left.Equals(right);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are not equal.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(PrimitiveValidationResult left, PrimitiveValidationResult right)
    {
        return !(left == right);
    }
}
