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
    private static readonly OpResult _Success = new(true, null);

    private readonly bool _isSuccess;
    private readonly ResultError? _error;

    private OpResult(bool isSuccess, ResultError? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    public ResultError? Error => _error;

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = _error;
        return !_isSuccess;
    }

    public TResult Match<TResult>(Func<TResult> success, Func<ResultError, TResult> failure) =>
        _isSuccess ? success() : failure(_error!);

    public OpResult OnSuccess(Action action)
    {
        if (_isSuccess)
            action();
        return this;
    }

    public OpResult OnFailure(Action<ResultError> action)
    {
        if (!_isSuccess)
            action(_error!);
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
    public bool Equals(OpResult other) => _isSuccess == other._isSuccess && Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is OpResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_isSuccess, _error);

    public static bool operator ==(OpResult left, OpResult right) => left.Equals(right);

    public static bool operator !=(OpResult left, OpResult right) => !left.Equals(right);
}
