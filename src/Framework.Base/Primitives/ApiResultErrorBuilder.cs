// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Builder for accumulating multiple errors before failing.
/// Useful for validation scenarios.
/// </summary>
[PublicAPI]
public ref struct ApiResultErrorBuilder
{
    private List<ResultError>? _errors;

    public readonly bool HasErrors => _errors is { Count: > 0 };

    public void Add(ResultError error)
    {
        _errors ??= [];
        _errors.Add(error);
    }

    public readonly ApiResult<T> ToApiResult<T>(T successValue)
    {
        return HasErrors ? new AggregateError { Errors = _errors! } : successValue;
    }

    public readonly ApiResult ToApiResult()
    {
        return HasErrors ? ApiResult.Fail(new AggregateError { Errors = _errors! }) : ApiResult.Ok();
    }
}
