// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail, with no return value.
/// </summary>
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

    /// <summary>The error. Throws if IsSuccess.</summary>
    public TError Error =>
        IsFailure ? _error! : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        error = _error;
        return IsFailure;
    }

    /// <summary>Pattern match on success or failure.</summary>
    public TResult Match<TResult>(Func<TResult> success, Func<TError, TResult> failure) =>
        IsFailure ? failure(_error!) : success();

    /// <summary>Execute action on success.</summary>
    public Result<TError> OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Execute action on failure.</summary>
    public Result<TError> OnFailure(Action<TError> action)
    {
        if (IsFailure)
        {
            action(_error!);
        }

        return this;
    }

    // Factory methods

    public static Result<TError> Ok() => _Success;

    public static Result<TError> Fail(TError error) => new(false, error);

    [Obsolete("Use Ok() instead")]
    public static Result<TError> Success() => Ok();

    // Implicit conversion

    public static implicit operator Result<TError>(TError error) => Fail(error);

    // Equality

    public bool Equals(Result<TError> other) =>
        IsSuccess == other.IsSuccess && EqualityComparer<TError?>.Default.Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is Result<TError> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _error);

    public static bool operator ==(Result<TError> left, Result<TError> right) => left.Equals(right);

    public static bool operator !=(Result<TError> left, Result<TError> right) => !left.Equals(right);
}
