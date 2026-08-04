// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Builder for accumulating multiple errors before failing.
/// Useful for validation scenarios.
/// </summary>
[PublicAPI]
public ref struct ApiResultErrorBuilder
{
    private List<ApiResultError>? _errors;

    /// <summary><see langword="true"/> if at least one error has been accumulated.</summary>
    public readonly bool HasErrors => _errors is { Count: > 0 };

    /// <summary>Accumulates an error to be reported when the builder is materialized into a result.</summary>
    /// <param name="error">The error to accumulate.</param>
    public void Add(ApiResultError error)
    {
        _errors ??= [];
        _errors.Add(Argument.IsNotNull(error));
    }

    /// <summary>
    /// Materializes the builder into an <see cref="ApiResult{T}"/>: a failure carrying the sole error or an
    /// <see cref="AggregateError"/> when multiple errors were accumulated, otherwise a success holding <paramref name="successValue"/>.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="successValue">The value used when no errors were accumulated.</param>
    /// <returns>A failed result when <see cref="HasErrors"/> is <see langword="true"/>; otherwise a successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when no errors were accumulated and <paramref name="successValue"/> is <see langword="null"/>.</exception>
    public readonly ApiResult<T> ToApiResult<T>(T successValue)
    {
        return _errors switch
        {
            null or { Count: 0 } => successValue,
            [var error] => error,
            _ => new AggregateError { Errors = _errors },
        };
    }

    /// <summary>
    /// Materializes the builder into a non-generic <see cref="ApiResult"/>: a failure carrying the sole error or an
    /// <see cref="AggregateError"/> when multiple errors were accumulated, otherwise a success.
    /// </summary>
    /// <returns>A failed result when <see cref="HasErrors"/> is <see langword="true"/>; otherwise a successful result.</returns>
    public readonly ApiResult ToApiResult()
    {
        return _errors switch
        {
            null or { Count: 0 } => ApiResult.Ok(),
            [var error] => ApiResult.Fail(error),
            _ => ApiResult.Fail(new AggregateError { Errors = _errors }),
        };
    }
}
