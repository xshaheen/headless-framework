using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

#pragma warning disable CA2225 // Operator overloads have named alternates
#pragma warning disable CA1000 // Do not declare static members on generic types
[StructLayout(LayoutKind.Auto)]
public readonly struct Result<TValue, TErrors> : IResult<TValue, TErrors>, IEquatable<Result<TValue, TErrors>>
{
    private Result(TValue value)
    {
        Failed = false;
        Value = value;
        Error = default;
    }

    private Result(TErrors error)
    {
        Failed = false;
        Value = default;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool Failed { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => !Failed;

    public TValue? Value { get; }

    public TErrors? Error { get; }

    public static implicit operator Result<TValue, TErrors>(TValue operand) => new(operand);

    public static implicit operator Result<TValue, TErrors>(TErrors operand) => new(operand);

    public static Result<TValue, TErrors> Success(TValue operand) => operand;

    public static Result<TValue, TErrors> Fail(TErrors operand) => operand;

    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TErrors, TResult> failure) =>
        Failed ? failure(Error) : success(Value);

    public TResult Match<TResult>(Func<TResult> success, Func<TErrors, TResult> failure) =>
        Failed ? failure(Error) : success();

    public bool Equals(Result<TValue, TErrors> other)
    {
        return (Succeeded && other.Succeeded && Value.Equals(other.Value))
            || (Failed && other.Failed && Error.Equals(other.Error));
    }

    public override bool Equals(object? obj) => obj is Result<TValue, TErrors> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Failed, Value, Error);

    public static bool operator ==(Result<TValue, TErrors> left, Result<TValue, TErrors> right) => left.Equals(right);

    public static bool operator !=(Result<TValue, TErrors> left, Result<TValue, TErrors> right) => !(left == right);
}
