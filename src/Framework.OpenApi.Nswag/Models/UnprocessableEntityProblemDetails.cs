// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Api.Models;

/// <summary>
/// Problem details schema for 422 Unprocessable Entity responses.
/// </summary>
public sealed class UnprocessableEntityProblemDetails : HeadlessProblemDetails
{
    public required Dictionary<string, List<ErrorDescriptor>> Errors { get; init; }
}
