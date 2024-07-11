using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

[StructLayout(LayoutKind.Auto)]
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

    public Result<TErrors> FromTErrors(TErrors operand) => operand;

    public Result<TErrors> Success() => new();

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
