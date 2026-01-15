// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Framework.Checks;

namespace Framework.Primitives;

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

    /// <summary>The success value. Throws if IsFailure.</summary>
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

    /// <summary>The error. Throws if IsSuccess.</summary>
    public ResultError Error =>
        !IsSuccess ? _error! : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Try to get the value without throwing.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = _error;
        return !IsSuccess;
    }

    /// <summary>Get the value or a default if failed.</summary>
    public T GetValueOrDefault(T defaultValue) => IsSuccess ? _value! : defaultValue;

    /// <summary>Pattern match on success or failure.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure)
    {
        return IsSuccess ? success(_value!) : failure(_error!);
    }

    /// <summary>Transform success value.</summary>
    public ApiResult<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        return IsSuccess ? ApiResult<TOut>.Ok(mapper(_value!)) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Chain operations that may fail.</summary>
    public ApiResult<TOut> Bind<TOut>(Func<T, ApiResult<TOut>> binder)
    {
        return IsSuccess ? binder(_value!) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Execute action on success.</summary>
    public ApiResult<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Execute action on failure.</summary>
    public ApiResult<T> OnFailure(Action<ResultError> action)
    {
        if (!IsSuccess)
        {
            action(_error!);
        }

        return this;
    }

    // Factory methods

    public static ApiResult<T> Ok(T value) => new(value);

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

    public static implicit operator ApiResult<T>(T value) => Ok(value);

    public static implicit operator ApiResult<T>(ResultError error) => Fail(error);

    /// <summary>Convert to non-generic result (discards value, keeps success/error state).</summary>
    public static implicit operator ApiResult(ApiResult<T> result)
    {
        return result.IsSuccess ? ApiResult.Ok() : ApiResult.Fail(result._error!);
    }

    // Equality

    public bool Equals(ApiResult<T> other)
    {
        return IsSuccess == other.IsSuccess
            && EqualityComparer<T?>.Default.Equals(_value, other._value)
            && Equals(_error, other._error);
    }

    public override bool Equals(object? obj) => obj is ApiResult<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, _error);

    public static bool operator ==(ApiResult<T> left, ApiResult<T> right) => left.Equals(right);

    public static bool operator !=(ApiResult<T> left, ApiResult<T> right) => !left.Equals(right);
}
