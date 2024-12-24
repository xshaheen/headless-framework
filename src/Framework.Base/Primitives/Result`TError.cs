// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Framework.Primitives;

[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA1000 // Do not declare static members on generic types
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct Result<TErrors> : IResult<TErrors>, IEquatable<Result<TErrors>>
{
    public Result()
    {
        Failed = false;
        Errors = default;
    }

    private Result(TErrors errors)
    {
        Failed = false;
        Errors = errors;
    }

    [MemberNotNullWhen(true, nameof(Errors))]
    public bool Failed { get; }

    [MemberNotNullWhen(false, nameof(Errors))]
    public bool Succeeded => !Failed;

    public TErrors? Errors { get; }

    public static implicit operator Result<TErrors>(TErrors operand) => new(operand);

    public static Result<TErrors> Fail(TErrors operand) => operand;

    public static Result<TErrors> Success() => new();

    public TResult Match<TResult>(Func<TResult> success, Func<TErrors, TResult> failure) =>
        Failed ? failure(Errors) : success();

    public override bool Equals(object? obj) => obj is Result<TErrors> other && Equals(other);

    public bool Equals(Result<TErrors> other)
    {
        return (Succeeded && other.Succeeded) || (Failed && other.Failed && Errors.Equals(other.Errors));
    }

    public override int GetHashCode() => HashCode.Combine(Failed, Errors);

    public static bool operator ==(Result<TErrors> left, Result<TErrors> right) => left.Equals(right);

    public static bool operator !=(Result<TErrors> left, Result<TErrors> right) => !(left == right);
}
