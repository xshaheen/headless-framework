// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail.
/// Success contains a value; failure contains an error.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
#pragma warning disable CA1000 // Do not declare static members on generic types
public readonly struct ApiResult<T> : IEquatable<ApiResult<T>>
{
    private readonly T? _value;
    private readonly ResultError? _error;

    private ApiResult(T value)
    {
        Argument.IsNotNull(value);
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private ApiResult(ResultError error)
    {
        _value = default;
        _error = Argument.IsNotNull(error);
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
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

    /// <summary>The error describing the failure.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a success (<see cref="IsSuccess"/> is <see langword="true"/>).</exception>
    public ResultError Error =>
        !IsSuccess ? _error! : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Tries to get the value without throwing.</summary>
    /// <param name="value">When this method returns <see langword="true"/>, the success value; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a success; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = _error;
        return !IsSuccess;
    }

    /// <summary>Gets the success value, or <paramref name="defaultValue"/> when the result is a failure.</summary>
    /// <param name="defaultValue">The value to return when the result is a failure.</param>
    /// <returns>The success value when successful; otherwise <paramref name="defaultValue"/>.</returns>
    public T GetValueOrDefault(T defaultValue) => IsSuccess ? _value! : defaultValue;

    /// <summary>Invokes <paramref name="success"/> with the value or <paramref name="failure"/> with the error, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success, receiving the value.</param>
    /// <param name="failure">The function invoked on failure, receiving the error.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure)
    {
        return IsSuccess ? success(_value!) : failure(_error!);
    }

    /// <summary>Transforms the success value with <paramref name="mapper"/>, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The mapped value type.</typeparam>
    /// <param name="mapper">The projection applied to the success value.</param>
    /// <returns>A successful result holding the mapped value, or the original failure.</returns>
    public ApiResult<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        return IsSuccess ? ApiResult<TOut>.Ok(mapper(_value!)) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Chains another result-producing operation on success, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
    /// <param name="binder">The function applied to the success value, producing the next result.</param>
    /// <returns>The result produced by <paramref name="binder"/>, or the original failure.</returns>
    public ApiResult<TOut> Bind<TOut>(Func<T, ApiResult<TOut>> binder)
    {
        return IsSuccess ? binder(_value!) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Invokes <paramref name="action"/> with the value when successful, then returns this result.</summary>
    /// <param name="action">The action to run on success, receiving the value.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult<T> OnSuccess(Action<T> action)
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
    public ApiResult<T> OnFailure(Action<ResultError> action)
    {
        if (!IsSuccess)
        {
            action(_error!);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Ok(T value) => new(value);

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Fail(ResultError error) => new(error);

    // Convenience factories

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a missing entity.
    /// </summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key or identifier used to look up the entity.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="NotFoundError"/> describing the missing entity.
    /// </returns>
    public static ApiResult<T> NotFound(string entity, string key)
    {
        return new ApiResult<T>(new NotFoundError { Entity = entity, Key = key });
    }

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a conflict.
    /// </summary>
    /// <param name="code">A machine-readable code describing the type of conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ConflictError"/> with the provided details.
    /// </returns>
    public static ApiResult<T> Conflict(string code, string message)
    {
        return new ApiResult<T>(new ConflictError(code, message));
    }

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing validation errors.
    /// </summary>
    /// <param name="errors">An array of field-error pairs representing the validation issues.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ValidationError"/> with the provided field errors.
    /// </returns>
    public static ApiResult<T> ValidationFailed(params (string Field, string Error)[] errors)
    {
        return new ApiResult<T>(ValidationError.FromFields(errors));
    }

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a forbidden operation.
    /// </summary>
    /// <param name="reason">The reason why the operation is not allowed.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ForbiddenError"/> with the provided reason.
    /// </returns>
    public static ApiResult<T> Forbidden(string reason) => new(new ForbiddenError { Reason = reason });

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing an unauthorized operation.
    /// </summary>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing an <see cref="UnauthorizedError"/>.
    /// </returns>
    public static ApiResult<T> Unauthorized() => new(UnauthorizedError.Instance);

    // Implicit conversions

    /// <summary>Implicitly wraps a value in a successful <see cref="ApiResult{T}"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/> carrying <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator ApiResult<T>(T value) => Ok(value);

    /// <summary>Implicitly wraps an error in a failed <see cref="ApiResult{T}"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/> carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator ApiResult<T>(ResultError error) => Fail(error);

    /// <summary>Converts to the non-generic <see cref="ApiResult"/> (discards the value, keeps the success/error state).</summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An <see cref="ApiResult"/> with the same success/error state.</returns>
    public static implicit operator ApiResult(ApiResult<T> result)
    {
        return result.IsSuccess ? ApiResult.Ok() : ApiResult.Fail(result._error!);
    }

    // Equality

    /// <summary>Determines whether this result equals <paramref name="other"/> in success state, value, and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state, value, and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(ApiResult<T> other)
    {
        return IsSuccess == other.IsSuccess
            && EqualityComparer<T?>.Default.Equals(_value, other._value)
            && Equals(_error, other._error);
    }

    /// <summary>Determines whether <paramref name="obj"/> is an <see cref="ApiResult{T}"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="ApiResult{T}"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is ApiResult<T> other && Equals(other);

    /// <summary>Returns a hash code derived from the success state, value, and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, _error);

    /// <summary>Determines whether two <see cref="ApiResult{T}"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ApiResult<T> left, ApiResult<T> right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="ApiResult{T}"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ApiResult<T> left, ApiResult<T> right) => !left.Equals(right);
}
