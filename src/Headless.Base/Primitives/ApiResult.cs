// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation with no return value.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct ApiResult : IEquatable<ApiResult>
{
    private static readonly ApiResult _Success = new(isSuccess: true, error: null);

    private ApiResult(bool isSuccess, ResultError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    public ResultError? Error { get; }

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = Error;
        return !IsSuccess;
    }

    public TResult Match<TResult>(Func<TResult> success, Func<ResultError, TResult> failure)
    {
        return IsSuccess ? success() : failure(Error!);
    }

    public ApiResult OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    public ApiResult OnFailure(Action<ResultError> action)
    {
        if (!IsSuccess)
        {
            action(Error!);
        }

        return this;
    }

    // Factory methods

    public static ApiResult Ok() => _Success;

    public static ApiResult Fail(ResultError error) => new(false, error);

    // Generic factory methods (type inference)

    /// <summary>Create success result with inferred type.</summary>
    public static ApiResult<T> Ok<T>(T value) => ApiResult<T>.Ok(value);

    /// <summary>Create failure result with inferred type.</summary>
    public static ApiResult<T> Fail<T>(ResultError error) => ApiResult<T>.Fail(error);

    public static ApiResult NotFound(string entity, string key)
    {
        return Fail(new NotFoundError { Entity = entity, Key = key });
    }

    public static ApiResult Conflict(string code, string message)
    {
        return Fail(new ConflictError(code, message));
    }

    public static ApiResult Forbidden(string reason)
    {
        return Fail(new ForbiddenError { Reason = reason });
    }

    public static ApiResult Unauthorized()
    {
        return Fail(UnauthorizedError.Instance);
    }

    // Implicit from error
    public static implicit operator ApiResult(ResultError error) => Fail(error);

    // Equality
    public bool Equals(ApiResult other) => IsSuccess == other.IsSuccess && Equals(Error, other.Error);

    public override bool Equals(object? obj) => obj is ApiResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error);

    public static bool operator ==(ApiResult left, ApiResult right) => left.Equals(right);

    public static bool operator !=(ApiResult left, ApiResult right) => !left.Equals(right);
}
