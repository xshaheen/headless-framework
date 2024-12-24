// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

[PublicAPI]
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct NoDataResult : IResult<IReadOnlyList<ErrorDescriptor>>, IEquatable<NoDataResult>
{
    private static readonly NoDataResult _Success = new() { Succeeded = true };

    public NoDataResult() { }

    public bool Succeeded { get; private init; } = false;

    public bool Failed => !Succeeded;

    public IReadOnlyList<ErrorDescriptor> Errors { get; private init; } = Array.Empty<ErrorDescriptor>();

    public TResult Match<TResult>(Func<TResult> success, Func<IReadOnlyList<ErrorDescriptor>, TResult> failure)
    {
        return Succeeded ? success() : failure(Errors);
    }

    public bool Equals(NoDataResult other) => Succeeded == other.Succeeded && Errors.SequenceEqual(other.Errors);

    public override bool Equals(object? obj) => obj is NoDataResult other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var error in Errors)
        {
            hashCode.Add(error);
        }

        return HashCode.Combine(Succeeded, hashCode.ToHashCode());
    }

    public static bool operator ==(NoDataResult left, NoDataResult right) => left.Equals(right);

    public static bool operator !=(NoDataResult left, NoDataResult right) => !(left == right);

    public static implicit operator NoDataResult(ErrorDescriptor operand) => Failure([operand]);

    public static implicit operator NoDataResult(List<ErrorDescriptor> operand) => Failure(operand);

    public static implicit operator NoDataResult(ErrorDescriptor[] operand) => Failure(operand);

    public static NoDataResult Success() => _Success;

    public static NoDataResult Failure(IReadOnlyList<ErrorDescriptor> errors) =>
        new() { Succeeded = false, Errors = errors };

    public static NoDataResult Failure(ErrorDescriptor error) => new() { Succeeded = false, Errors = [error] };
}
