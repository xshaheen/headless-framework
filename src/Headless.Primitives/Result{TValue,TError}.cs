// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail.
/// Success contains a value of type <typeparamref name="TValue"/>; failure contains an error of type <typeparamref name="TError"/>.
/// </summary>
/// <typeparam name="TValue">The value type carried on success.</typeparam>
/// <typeparam name="TError">The error type carried on failure.</typeparam>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
#pragma warning disable CA1000 // Do not declare static members on generic types
public readonly struct Result<TValue, TError> : IEquatable<Result<TValue, TError>>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(TValue value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>True if operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>True if operation failed.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>The success value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure (<see cref="IsFailure"/> is <see langword="true"/>).</exception>
    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

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

            // A default(Result<TValue, TError>) is a failure state with no error; throw clearly instead of a downstream NRE.
            if (_error is null)
            {
                throw new InvalidOperationException(
                    "Result<TValue, TError> was not properly initialized. Error was accessed on a default instance."
                );
            }

            return _error;
        }
    }

    /// <summary>Tries to get the value without throwing.</summary>
    /// <param name="value">When this method returns <see langword="true"/>, the success value; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a success; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        error = _error;
        return IsFailure;
    }

    /// <summary>Gets the success value, or <paramref name="defaultValue"/> when the result is a failure.</summary>
    /// <param name="defaultValue">The value to return when the result is a failure.</param>
    /// <returns>The success value when successful; otherwise <paramref name="defaultValue"/>.</returns>
    public TValue GetValueOrDefault(TValue defaultValue)
    {
        return IsSuccess ? _value! : defaultValue;
    }

    /// <summary>Invokes <paramref name="success"/> with the value or <paramref name="failure"/> with the error, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success, receiving the value.</param>
    /// <param name="failure">The function invoked on failure, receiving the error.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure)
    {
        return IsFailure ? failure(Error) : success(_value!);
    }

    /// <summary>Invokes <paramref name="success"/> (ignoring the value) or <paramref name="failure"/> with the error, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success.</param>
    /// <param name="failure">The function invoked on failure, receiving the error.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<TResult> success, Func<TError, TResult> failure)
    {
        return IsFailure ? failure(Error) : success();
    }

    /// <summary>Transforms the success value with <paramref name="mapper"/>, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The mapped value type.</typeparam>
    /// <param name="mapper">The projection applied to the success value.</param>
    /// <returns>A successful result holding the mapped value, or the original failure.</returns>
    public Result<TOut, TError> Map<TOut>(Func<TValue, TOut> mapper)
    {
        return IsSuccess ? Result<TOut, TError>.Ok(mapper(_value!)) : Result<TOut, TError>.Fail(_error!);
    }

    /// <summary>Chains another result-producing operation on success, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
    /// <param name="binder">The function applied to the success value, producing the next result.</param>
    /// <returns>The result produced by <paramref name="binder"/>, or the original failure.</returns>
    public Result<TOut, TError> Bind<TOut>(Func<TValue, Result<TOut, TError>> binder)
    {
        return IsSuccess ? binder(_value!) : Result<TOut, TError>.Fail(_error!);
    }

    /// <summary>Invokes <paramref name="action"/> with the value when successful, then returns this result.</summary>
    /// <param name="action">The action to run on success, receiving the value.</param>
    /// <returns>This result, to allow chaining.</returns>
    public Result<TValue, TError> OnSuccess(Action<TValue> action)
    {
        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Invokes <paramref name="action"/> with the error when failed, then returns this result.</summary>
    /// <param name="action">The action to run on failure, receiving the error.</param>
    /// <returns>This result, to allow chaining.</returns>
    public Result<TValue, TError> OnFailure(Action<TError> action)
    {
        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="Result{TValue, TError}"/>.</returns>
    public static Result<TValue, TError> Ok(TValue value)
    {
        return new(value);
    }

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TValue, TError}"/>.</returns>
    public static Result<TValue, TError> Fail(TError error)
    {
        return new(error);
    }

    // Implicit conversions

    /// <summary>Implicitly wraps a value in a successful <see cref="Result{TValue, TError}"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result carrying <paramref name="value"/>.</returns>
    public static implicit operator Result<TValue, TError>(TValue value) => Ok(value);

    /// <summary>Implicitly wraps an error in a failed <see cref="Result{TValue, TError}"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed result carrying <paramref name="error"/>.</returns>
    public static implicit operator Result<TValue, TError>(TError error) => Fail(error);

    // Equality

    /// <summary>Determines whether this result equals <paramref name="other"/> in success state, value, and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state, value, and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(Result<TValue, TError> other)
    {
        return IsSuccess == other.IsSuccess
            && EqualityComparer<TValue?>.Default.Equals(_value, other._value)
            && EqualityComparer<TError?>.Default.Equals(_error, other._error);
    }

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="Result{TValue, TError}"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="Result{TValue, TError}"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Result<TValue, TError> other && Equals(other);
    }

    /// <summary>Returns a hash code derived from the success state, value, and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(IsSuccess, _value, _error);
    }

    /// <summary>Determines whether two <see cref="Result{TValue, TError}"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Result<TValue, TError> left, Result<TValue, TError> right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="Result{TValue, TError}"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Result<TValue, TError> left, Result<TValue, TError> right) => !left.Equals(right);
}
