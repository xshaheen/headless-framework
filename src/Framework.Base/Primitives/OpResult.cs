// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Framework.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail.
/// Success contains a value; failure contains an error.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
#pragma warning disable CA1000 // Do not declare static members on generic types
public readonly struct OpResult<T> : IEquatable<OpResult<T>>
{
    private readonly T? _value;
    private readonly ResultError? _error;
    private readonly bool _isSuccess;

    private OpResult(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        _error = null;
        _isSuccess = true;
    }

    private OpResult(ResultError error)
    {
        _value = default;
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _isSuccess = false;
    }

    /// <summary>True if operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    /// <summary>True if operation failed.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    /// <summary>The success value. Throws if IsFailure.</summary>
    public T Value =>
        _isSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

    /// <summary>The error. Throws if IsSuccess.</summary>
    public ResultError Error =>
        !_isSuccess ? _error! : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Try to get the value without throwing.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _isSuccess;
    }

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = _error;
        return !_isSuccess;
    }

    /// <summary>Pattern match on success or failure.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure) =>
        _isSuccess ? success(_value!) : failure(_error!);

    /// <summary>Transform success value.</summary>
    public OpResult<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        _isSuccess ? OpResult<TOut>.Ok(mapper(_value!)) : OpResult<TOut>.Fail(_error!);

    /// <summary>Chain operations that may fail.</summary>
    public OpResult<TOut> Bind<TOut>(Func<T, OpResult<TOut>> binder) =>
        _isSuccess ? binder(_value!) : OpResult<TOut>.Fail(_error!);

    /// <summary>Execute action on success.</summary>
    public OpResult<T> OnSuccess(Action<T> action)
    {
        if (_isSuccess)
            action(_value!);
        return this;
    }

    /// <summary>Execute action on failure.</summary>
    public OpResult<T> OnFailure(Action<ResultError> action)
    {
        if (!_isSuccess)
            action(_error!);
        return this;
    }

    // Factory methods

    public static OpResult<T> Ok(T value) => new(value);

    public static OpResult<T> Fail(ResultError error) => new(error);

    // Convenience factories

    public static OpResult<T> NotFound(string entity, string key) =>
        new(new NotFoundError { Entity = entity, Key = key });

    public static OpResult<T> Conflict(string code, string message) => new(new ConflictError(code, message));

    public static OpResult<T> ValidationFailed(params (string Field, string Error)[] errors) =>
        new(ValidationError.FromFields(errors));

    public static OpResult<T> Forbidden(string reason) => new(new ForbiddenError { Reason = reason });

    public static OpResult<T> Unauthorized() => new(UnauthorizedError.Instance);

    // Implicit conversions

    public static implicit operator OpResult<T>(T value) => Ok(value);

    public static implicit operator OpResult<T>(ResultError error) => Fail(error);

    // Equality

    public bool Equals(OpResult<T> other) =>
        _isSuccess == other._isSuccess
        && EqualityComparer<T?>.Default.Equals(_value, other._value)
        && Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is OpResult<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_isSuccess, _value, _error);

    public static bool operator ==(OpResult<T> left, OpResult<T> right) => left.Equals(right);

    public static bool operator !=(OpResult<T> left, OpResult<T> right) => !left.Equals(right);
}
