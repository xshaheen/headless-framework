// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail, with no return value. Success carries no data;
/// failure carries an error of type <typeparamref name="TError"/>.
/// </summary>
/// <typeparam name="TError">The error type carried on failure.</typeparam>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA1000 // Do not declare static members on generic types
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct Result<TError> : IEquatable<Result<TError>>
{
    private static readonly Result<TError> _Success = new(isSuccess: true, error: default);

    private readonly TError? _error;

    private Result(bool isSuccess, TError? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>True if operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>True if operation failed.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>The error describing the failure.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the result is a success (<see cref="IsSuccess"/> is <see langword="true"/>), or when accessed on a
    /// default-initialized instance (which is a failure state carrying no error).
    /// </exception>
    public TError Error
    {
        get
        {
            if (IsSuccess)
            {
                throw new InvalidOperationException("Cannot access Error on successful result.");
            }

            // A default(Result<TError>) is a failure state with no error; throw a clear error instead of a downstream NRE.
            if (_error is null)
            {
                throw new InvalidOperationException(
                    "Result<TError> was not properly initialized. Error was accessed on a default instance."
                );
            }

            return _error;
        }
    }

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        error = _error;
        return IsFailure;
    }

    /// <summary>Invokes <paramref name="success"/> when successful or <paramref name="failure"/> when failed, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success.</param>
    /// <param name="failure">The function invoked on failure, receiving the error.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<TResult> success, Func<TError, TResult> failure) =>
        IsFailure ? failure(Error) : success();

    /// <summary>Invokes <paramref name="action"/> when the result is a success, then returns this result.</summary>
    /// <param name="action">The action to run on success.</param>
    /// <returns>This result, to allow chaining.</returns>
    public Result<TError> OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Invokes <paramref name="action"/> with the error when failed, then returns this result.</summary>
    /// <param name="action">The action to run on failure, receiving the error.</param>
    /// <returns>This result, to allow chaining.</returns>
    public Result<TError> OnFailure(Action<TError> action)
    {
        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="Result{TError}"/>.</returns>
    public static Result<TError> Ok() => _Success;

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TError}"/>.</returns>
    public static Result<TError> Fail(TError error) => new(isSuccess: false, error: error);

    // Implicit conversion

    /// <summary>Implicitly wraps an error in a failed <see cref="Result{TError}"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TError}"/> carrying <paramref name="error"/>.</returns>
    public static implicit operator Result<TError>(TError error) => Fail(error);

    // Equality

    /// <summary>Determines whether this result equals <paramref name="other"/> in success state and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(Result<TError> other) =>
        IsSuccess == other.IsSuccess && EqualityComparer<TError?>.Default.Equals(_error, other._error);

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="Result{TError}"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="Result{TError}"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is Result<TError> other && Equals(other);

    /// <summary>Returns a hash code derived from the success state and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode() => HashCode.Combine(IsSuccess, _error);

    /// <summary>Determines whether two <see cref="Result{TError}"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Result<TError> left, Result<TError> right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="Result{TError}"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Result<TError> left, Result<TError> right) => !left.Equals(right);
}
