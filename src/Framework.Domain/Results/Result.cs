namespace Framework.Domain.Results;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
public readonly struct Result<TValue> : IEquatable<Result<TValue>>
{
    private readonly TValue? _value;
    private readonly Error? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TValue Value =>
        IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value on failed result");

    public Error Error =>
        IsFailure ? _error! : throw new InvalidOperationException("Cannot access Error on successful result");

    private Result(TValue value) => (IsSuccess, _value, _error) = (true, value, null);

    private Result(Error error) => (IsSuccess, _value, _error) = (false, default, error);

    public static Result<TValue> Success(TValue value) => new(value);

    public static Result<TValue> Failure(Error error) => new(error);

    public static Result<TValue> FromValue(TValue value) => Success(value);

    public static Result<TValue> FromError(Error error) => Failure(error);

#pragma warning disable CA2225 // Allow implicit conversion from TValue to Result<TValue>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
#pragma warning restore CA2225

    public static implicit operator Result<TValue>(Error error) => Failure(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }

    public Result<TResult> Map<TResult>(Func<TValue, TResult> mapper)
    {
        return IsSuccess ? Result<TResult>.Success(mapper(_value!)) : Result<TResult>.Failure(_error!);
    }

    public async Task<TResult> MatchAsync<TResult>(
        Func<TValue, Task<TResult>> onSuccess,
        Func<Error, Task<TResult>> onFailure
    )
    {
        if (IsSuccess)
        {
            var success = onSuccess(_value!);

            return await success.ConfigureAwait(false);
        }

        return await onFailure(_error!).ConfigureAwait(false);
    }

    public bool Equals(Result<TValue> other)
    {
        return IsSuccess == other.IsSuccess
            && EqualityComparer<TValue?>.Default.Equals(_value, other._value)
            && Equals(_error, other._error);
    }

    public override bool Equals(object? obj) => obj is Result<TValue> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, _error);

    public static bool operator ==(Result<TValue> left, Result<TValue> right) => left.Equals(right);

    public static bool operator !=(Result<TValue> left, Result<TValue> right) => !left.Equals(right);
}
