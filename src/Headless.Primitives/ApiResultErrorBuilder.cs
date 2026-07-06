// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Builder for accumulating multiple errors before failing.
/// Useful for validation scenarios.
/// </summary>
[PublicAPI]
public ref struct ApiResultErrorBuilder
{
    private List<ResultError>? _errors;

    /// <summary><see langword="true"/> if at least one error has been accumulated.</summary>
    public readonly bool HasErrors => _errors is { Count: > 0 };

    /// <summary>Accumulates an error to be reported when the builder is materialized into a result.</summary>
    /// <param name="error">The error to accumulate.</param>
    public void Add(ResultError error)
    {
        _errors ??= [];
        _errors.Add(error);
    }

    /// <summary>
    /// Materializes the builder into an <see cref="ApiResult{T}"/>: a failure carrying an
    /// <see cref="AggregateError"/> if any errors were accumulated, otherwise a success holding <paramref name="successValue"/>.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="successValue">The value used when no errors were accumulated.</param>
    /// <returns>A failed result when <see cref="HasErrors"/> is <see langword="true"/>; otherwise a successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when no errors were accumulated and <paramref name="successValue"/> is <see langword="null"/>.</exception>
    public readonly ApiResult<T> ToApiResult<T>(T successValue)
    {
        return HasErrors ? new AggregateError { Errors = _errors! } : successValue;
    }

    /// <summary>
    /// Materializes the builder into a non-generic <see cref="ApiResult"/>: a failure carrying an
    /// <see cref="AggregateError"/> if any errors were accumulated, otherwise a success.
    /// </summary>
    /// <returns>A failed result when <see cref="HasErrors"/> is <see langword="true"/>; otherwise a successful result.</returns>
    public readonly ApiResult ToApiResult()
    {
        return HasErrors ? ApiResult.Fail(new AggregateError { Errors = _errors! }) : ApiResult.Ok();
    }
}
