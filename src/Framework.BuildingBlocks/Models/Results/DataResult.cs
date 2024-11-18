// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130, CA2225 // Operator overloads have named alternates
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public readonly struct DataResult<T> : IResult<T, IReadOnlyList<ErrorDescriptor>>, IEquatable<DataResult<T>>
{
    public DataResult()
    {
        Succeeded = false;
        Data = default;
    }

    [MemberNotNullWhen(true, nameof(Data))]
    public bool Succeeded { get; init; }

    [MemberNotNullWhen(false, nameof(Data))]
    public bool Failed => !Succeeded;

    public T? Data { get; init; }

    public IReadOnlyList<ErrorDescriptor> Errors { get; init; } = Array.Empty<ErrorDescriptor>();

    public TResult Match<TResult>(Func<TResult> success, Func<IReadOnlyList<ErrorDescriptor>, TResult> failure)
    {
        return Succeeded ? success() : failure(Errors);
    }

    public TResult Match<TResult>(Func<T, TResult> success, Func<IReadOnlyList<ErrorDescriptor>, TResult> failure)
    {
        return Succeeded ? success(Data!) : failure(Errors);
    }

    public bool Equals(DataResult<T> other)
    {
        return Succeeded == other.Succeeded
            && EqualityComparer<T?>.Default.Equals(Data, other.Data)
            && Errors.SequenceEqual(other.Errors);
    }

    public override bool Equals(object? obj)
    {
        return obj is DataResult<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        hashCode.Add(Succeeded);
        hashCode.Add(Data);

        foreach (var error in Errors)
        {
            hashCode.Add(error);
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(DataResult<T> left, DataResult<T> right) => left.Equals(right);

    public static bool operator !=(DataResult<T> left, DataResult<T> right) => !(left == right);

    public static implicit operator DataResult<T>(T operand) => new() { Succeeded = true, Data = operand };

    public static implicit operator DataResult<T>(ErrorDescriptor operand) =>
        new() { Succeeded = false, Errors = [operand] };

    public static implicit operator DataResult<T>(ErrorDescriptor[] operand) =>
        new() { Succeeded = false, Errors = operand };

    public static implicit operator DataResult<T>(List<ErrorDescriptor> operand) =>
        new() { Succeeded = false, Errors = operand };
}

public static class DataResult
{
    public static DataResult<T> Success<T>(T data)
    {
        return new() { Succeeded = true, Data = data };
    }

    public static DataResult<T> Failure<T>()
    {
        return new() { Succeeded = false };
    }

    public static DataResult<T> Failure<T>(IReadOnlyList<ErrorDescriptor> errors)
    {
        return new() { Succeeded = false, Errors = errors };
    }

    public static DataResult<T> Failure<T>(ErrorDescriptor error)
    {
        return new() { Succeeded = false, Errors = [error] };
    }
}
