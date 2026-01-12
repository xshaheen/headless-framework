// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api.Models;

/// <summary>
/// Parameters for entity not found error.
/// </summary>
public sealed class EntityNotFoundProblemDetailsParams
{
    public required string Entity { get; init; }
    public required string Key { get; init; }
}

/// <summary>
/// Problem details schema for 404 Entity Not Found responses.
/// </summary>
public sealed class EntityNotFoundProblemDetails : HeadlessProblemDetails
{
    public required EntityNotFoundProblemDetailsParams Params { get; init; }
}
