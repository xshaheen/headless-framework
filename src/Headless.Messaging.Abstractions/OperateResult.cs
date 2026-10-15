// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

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
/// <param name="isBrokerUnreachable">
/// Whether the operation failed because the broker could not be reached, as opposed to the broker refusing it.
/// </param>
[PublicAPI]
public readonly struct OperateResult(
    bool succeeded,
    Exception? exception = null,
    OperateError? error = null,
    bool isBrokerUnreachable = false
) : IEquatable<OperateResult>
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
    /// Gets whether the operation failed because the broker could not be reached: a connection, socket, or timeout
    /// failure, or a broker that reports itself down. A refusal the broker answered with, such as a missing topic, a
    /// rejected message, or one too large, is <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// Only these failures count toward the host's broker reachability, so one message the broker refuses cannot take
    /// a host out of readiness. A transport sets it with <see cref="BrokerUnreachable" />.
    /// </remarks>
    public bool IsBrokerUnreachable { get; } = isBrokerUnreachable;

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
    /// Creates a failed <see cref="OperateResult"/> for an operation that could not reach the broker, which counts toward
    /// the host's broker reachability (see <see cref="IsBrokerUnreachable" />).
    /// </summary>
    /// <param name="ex">The connection, socket, or timeout failure.</param>
    /// <param name="errors">Optional structured error information describing the failure.</param>
    /// <returns>A failed <see cref="OperateResult"/> with <see cref="IsBrokerUnreachable" /> set.</returns>
    public static OperateResult BrokerUnreachable(Exception ex, OperateError? errors = null)
    {
        return new(succeeded: false, ex, errors, isBrokerUnreachable: true);
    }

    /// <summary>
    /// Returns a string representation of the operation result.
    /// </summary>
    /// <returns>
    /// "Succeeded" if the operation was successful; otherwise "Failed" with the error code.
    /// </returns>
    public override string ToString()
    {
        return Succeeded ? "Succeeded" : $"Failed : {_operateError?.Code}";
    }

    /// <summary>
    /// Determines whether this result equals another based on success status,
    /// the wrapped exception reference, and the structured error.
    /// </summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns>true if both results match on every field; otherwise false.</returns>
    public bool Equals(OperateResult other)
    {
        return Succeeded == other.Succeeded
            && IsBrokerUnreachable == other.IsBrokerUnreachable
            && ReferenceEquals(Exception, other.Exception)
            && Nullable.Equals(_operateError, other._operateError);
    }

    public override bool Equals(object? obj)
    {
        return obj is OperateResult other && Equals(other);
    }

    /// <summary>
    /// Serves as the default hash function for the <see cref="OperateResult"/> struct.
    /// Hashes the same fields <see cref="Equals(OperateResult)"/> compares.
    /// </summary>
    /// <returns>A hash code combining the error, success status, and exception information.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(_operateError, Succeeded, IsBrokerUnreachable, Exception);
    }

    public static bool operator ==(OperateResult left, OperateResult right) => left.Equals(right);

    public static bool operator !=(OperateResult left, OperateResult right) => !(left == right);
}

/// <summary>
/// Encapsulates structured error information from a failed operation.
/// This record provides a standardized way to report operation errors with code and description.
/// </summary>
[PublicAPI]
public readonly record struct OperateError
{
    /// <summary>
    /// Gets the error code identifying the type or source of the error.
    /// This might be a string representation of a numeric error code, a category name, or other identifier.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets a human-readable description of the error.
    /// This typically explains what went wrong and may include suggestions for resolution.
    /// </summary>
    public required string Description { get; init; }
}
