// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    /// <summary><see langword="true"/> if the operation succeeded; <see cref="Error"/> is then <see langword="null"/>.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary><see langword="true"/> if the operation failed; <see cref="Error"/> is then non-<see langword="null"/>.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>The error describing the failure, or <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public ResultError? Error { get; }

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = Error;
        return !IsSuccess;
    }

    /// <summary>Invokes <paramref name="success"/> when successful or <paramref name="failure"/> when failed, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success.</param>
    /// <param name="failure">The function invoked on failure, receiving the <see cref="Error"/>.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<TResult> success, Func<ResultError, TResult> failure)
    {
        return IsSuccess ? success() : failure(Error!);
    }

    /// <summary>Invokes <paramref name="action"/> when the result is a success, then returns this result.</summary>
    /// <param name="action">The action to run on success.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Invokes <paramref name="action"/> with the error when the result is a failure, then returns this result.</summary>
    /// <param name="action">The action to run on failure, receiving the <see cref="Error"/>.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult OnFailure(Action<ResultError> action)
    {
        if (!IsSuccess)
        {
            action(Error!);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="ApiResult"/>.</returns>
    public static ApiResult Ok() => _Success;

    /// <summary>Creates a failed result carrying the supplied error.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult"/>.</returns>
    public static ApiResult Fail(ResultError error) => new(false, error);

    // Generic factory methods (type inference)

    /// <summary>Creates a successful <see cref="ApiResult{T}"/> with an inferred value type.</summary>
    /// <typeparam name="T">The success value type, inferred from <paramref name="value"/>.</typeparam>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Ok<T>(T value) => ApiResult<T>.Ok(value);

    /// <summary>Creates a failed <see cref="ApiResult{T}"/> with an inferred value type.</summary>
    /// <typeparam name="T">The success value type that the result would otherwise carry.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Fail<T>(ResultError error) => ApiResult<T>.Fail(error);

    /// <summary>Creates a failed result representing a missing entity.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key or identifier used to look up the entity.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult NotFound(string entity, string key)
    {
        return Fail(new NotFoundError { Entity = entity, Key = key });
    }

    /// <summary>Creates a failed result representing a conflict.</summary>
    /// <param name="code">A machine-readable code describing the type of conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="ConflictError"/>.</returns>
    public static ApiResult Conflict(string code, string message)
    {
        return Fail(new ConflictError(code, message));
    }

    /// <summary>Creates a failed result representing a forbidden operation.</summary>
    /// <param name="reason">The reason why the operation is not allowed.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="ForbiddenError"/>.</returns>
    public static ApiResult Forbidden(string reason)
    {
        return Fail(new ForbiddenError { Reason = reason });
    }

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <returns>A failed <see cref="ApiResult"/> containing an <see cref="UnauthorizedError"/>.</returns>
    public static ApiResult Unauthorized()
    {
        return Fail(UnauthorizedError.Instance);
    }

    // Implicit from error
    /// <summary>Implicitly converts a <see cref="ResultError"/> into a failed <see cref="ApiResult"/>.</summary>
    /// <param name="error">The error to wrap.</param>
    /// <returns>A failed <see cref="ApiResult"/> carrying <paramref name="error"/>.</returns>
    public static implicit operator ApiResult(ResultError error) => Fail(error);

    // Equality
    /// <summary>Determines whether this result equals <paramref name="other"/> in success state and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(ApiResult other) => IsSuccess == other.IsSuccess && Equals(Error, other.Error);

    /// <summary>Determines whether <paramref name="obj"/> is an <see cref="ApiResult"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="ApiResult"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is ApiResult other && Equals(other);

    /// <summary>Returns a hash code derived from the success state and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error);

    /// <summary>Determines whether two <see cref="ApiResult"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ApiResult left, ApiResult right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="ApiResult"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ApiResult left, ApiResult right) => !left.Equals(right);
}
