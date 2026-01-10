// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Framework.Primitives;

/// <summary>
/// Represents the outcome of an operation with no return value.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct OpResult : IEquatable<OpResult>
{
    private static readonly OpResult _Success = new(isSuccess: true, error: null);

    private OpResult(bool isSuccess, ResultError? error)
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

    public TResult Match<TResult>(Func<TResult> success, Func<ResultError, TResult> failure) =>
        IsSuccess ? success() : failure(Error!);

    public OpResult OnSuccess(Action action)
    {
        if (IsSuccess)
            action();
        return this;
    }

    public OpResult OnFailure(Action<ResultError> action)
    {
        if (!IsSuccess)
            action(Error!);
        return this;
    }

    // Factory methods

    public static OpResult Ok() => _Success;

    public static OpResult Fail(ResultError error) => new(false, error);

    public static OpResult NotFound(string entity, string key) =>
        Fail(new NotFoundError { Entity = entity, Key = key });

    public static OpResult Conflict(string code, string message) => Fail(new ConflictError(code, message));

    public static OpResult Forbidden(string reason) => Fail(new ForbiddenError { Reason = reason });

    public static OpResult Unauthorized() => Fail(UnauthorizedError.Instance);

    // Implicit from error
    public static implicit operator OpResult(ResultError error) => Fail(error);

    // Equality
    public bool Equals(OpResult other) => IsSuccess == other.IsSuccess && Equals(Error, other.Error);

    public override bool Equals(object? obj) => obj is OpResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error);

    public static bool operator ==(OpResult left, OpResult right) => left.Equals(right);

    public static bool operator !=(OpResult left, OpResult right) => !left.Equals(right);
}
