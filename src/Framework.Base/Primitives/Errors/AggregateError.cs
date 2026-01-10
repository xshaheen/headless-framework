// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Multiple errors occurred. Useful for batch operations.
/// </summary>
[PublicAPI]
public sealed record AggregateError : ResultError
{
    public required IReadOnlyList<ResultError> Errors { get; init; }

    public override string Code => "aggregate:multiple_errors";
    public override string Message => $"{Errors.Count} errors occurred.";
}
