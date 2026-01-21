// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

/// <summary>
/// Represents the result of a message operation (publish or consume), including success/failure status and optional error details.
/// This struct is used throughout the messaging system to standardize how operation outcomes are reported.
/// </summary>
/// <remarks>
/// The <see cref="OperateResult"/> can represent:
/// <list type="bullet">
/// <item><description>Successful operations with <see cref="Succeeded"/> = true.</description></item>
/// <item><description>Failed operations with <see cref="Succeeded"/> = false, along with an <see cref="Exception"/> and optional <see cref="OperateError"/> details.</description></item>
/// </list>
/// Use the static <see cref="Success"/> property for successful results, or the static <see cref="Failed"/> method for failures.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="OperateResult"/> struct with the specified status and optional error information.
/// </remarks>
/// <param name="succeeded">A value indicating whether the operation succeeded.</param>
/// <param name="exception">The exception that occurred during the operation, or null if successful.</param>
/// <param name="error">Additional error details, or null if no structured error information is available.</param>
public readonly struct OperateResult(bool succeeded, Exception? exception = null, OperateError? error = null)
    : IEqualityComparer<OperateResult>,
        IEquatable<OperateResult>
{
    private readonly OperateError? _operateError = error;

    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Succeeded { get; } = succeeded;

    /// <summary>
    /// Gets or sets the exception that occurred during the operation.
    /// This is typically populated when <see cref="Succeeded"/> is false.
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// Gets a static <see cref="OperateResult"/> representing a successful operation.
    /// </summary>
    public static OperateResult Success => new(succeeded: true);

    /// <summary>
    /// Creates an <see cref="OperateResult"/> representing a failed operation.
    /// </summary>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="errors">Optional structured error information describing the failure.</param>
    /// <returns>A failed <see cref="OperateResult"/> containing the exception and error details.</returns>
    public static OperateResult Failed(Exception ex, OperateError? errors = null)
    {
        return new(succeeded: false, ex, errors);
    }

    /// <summary>
    /// Returns a string representation of the operation result.
    /// </summary>
    /// <returns>
    /// "Succeeded" if the operation was successful; otherwise "Failed" with the error code.
    /// </returns>
    public override string ToString() => Succeeded ? "Succeeded" : $"Failed : {_operateError?.Code}";

    /// <summary>
    /// Determines whether two <see cref="OperateResult"/> instances are equal based on their success status.
    /// </summary>
    /// <param name="x">The first result to compare.</param>
    /// <param name="y">The second result to compare.</param>
    /// <returns>true if both results have the same success status; otherwise false.</returns>
    public bool Equals(OperateResult x, OperateResult y) => x.Succeeded == y.Succeeded;

    /// <summary>
    /// Serves as the default hash function for the <see cref="OperateResult"/> struct.
    /// </summary>
    /// <param name="o">The result to compute the hash code for.</param>
    /// <returns>A hash code combining the error, success status, and exception information.</returns>
    public int GetHashCode(OperateResult o) => HashCode.Combine(o._operateError, o.Succeeded, o.Exception);

    public override bool Equals(object? obj) => obj is OperateResult other && Equals(other);

    public override int GetHashCode() => GetHashCode(this);

    public static bool operator ==(OperateResult left, OperateResult right) => left.Equals(right);

    public static bool operator !=(OperateResult left, OperateResult right) => !(left == right);

    public bool Equals(OperateResult other) => Equals(this, other);
}

/// <summary>
/// Encapsulates structured error information from a failed operation.
/// This record provides a standardized way to report operation errors with code and description.
/// </summary>
public record struct OperateError
{
    /// <summary>
    /// Gets or sets the error code identifying the type or source of the error.
    /// This might be a string representation of a numeric error code, a category name, or other identifier.
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of the error.
    /// This typically explains what went wrong and may include suggestions for resolution.
    /// </summary>
    public string Description { get; set; }
}
