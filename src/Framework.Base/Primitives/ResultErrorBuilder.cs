// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Builder for accumulating multiple errors before failing.
/// Useful for validation scenarios.
/// </summary>
[PublicAPI]
public ref struct ResultErrorBuilder
{
    private List<ResultError>? _errors;

    public readonly bool HasErrors => _errors is { Count: > 0 };

    public void Add(ResultError error)
    {
        _errors ??= [];
        _errors.Add(error);
    }

    public readonly OpResult<T> ToResult<T>(T successValue) =>
        HasErrors ? new AggregateError { Errors = _errors! } : successValue;

    public readonly OpResult ToResult() =>
        HasErrors ? OpResult.Fail(new AggregateError { Errors = _errors! }) : OpResult.Ok();
}
