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

    /// <summary>The success value. Throws if IsFailure.</summary>
    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

    /// <summary>The error. Throws if IsSuccess.</summary>
    public TError Error =>
        IsFailure ? _error! : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Try to get the value without throwing.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        error = _error;
        return IsFailure;
    }

    /// <summary>Get the value or a default if failed.</summary>
    public TValue GetValueOrDefault(TValue defaultValue) => IsSuccess ? _value! : defaultValue;

    /// <summary>Pattern match on success or failure.</summary>
    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure) =>
        IsFailure ? failure(_error!) : success(_value!);

    /// <summary>Pattern match on success or failure (ignoring value).</summary>
    public TResult Match<TResult>(Func<TResult> success, Func<TError, TResult> failure) =>
        IsFailure ? failure(_error!) : success();

    /// <summary>Transform success value.</summary>
    public Result<TOut, TError> Map<TOut>(Func<TValue, TOut> mapper) =>
        IsSuccess ? Result<TOut, TError>.Ok(mapper(_value!)) : Result<TOut, TError>.Fail(_error!);

    /// <summary>Chain operations that may fail.</summary>
    public Result<TOut, TError> Bind<TOut>(Func<TValue, Result<TOut, TError>> binder) =>
        IsSuccess ? binder(_value!) : Result<TOut, TError>.Fail(_error!);

    /// <summary>Execute action on success.</summary>
    public Result<TValue, TError> OnSuccess(Action<TValue> action)
    {
        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Execute action on failure.</summary>
    public Result<TValue, TError> OnFailure(Action<TError> action)
    {
        if (IsFailure)
        {
            action(_error!);
        }

        return this;
    }

    // Factory methods

    public static Result<TValue, TError> Ok(TValue value) => new(value);

    public static Result<TValue, TError> Fail(TError error) => new(error);

    [Obsolete("Use Ok() instead")]
    public static Result<TValue, TError> Success(TValue value) => Ok(value);

    // Implicit conversions

    public static implicit operator Result<TValue, TError>(TValue value) => Ok(value);

    public static implicit operator Result<TValue, TError>(TError error) => Fail(error);

    // Equality

    public bool Equals(Result<TValue, TError> other) =>
        IsSuccess == other.IsSuccess
        && EqualityComparer<TValue?>.Default.Equals(_value, other._value)
        && EqualityComparer<TError?>.Default.Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is Result<TValue, TError> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, _error);

    public static bool operator ==(Result<TValue, TError> left, Result<TValue, TError> right) => left.Equals(right);

    public static bool operator !=(Result<TValue, TError> left, Result<TValue, TError> right) => !left.Equals(right);
}
