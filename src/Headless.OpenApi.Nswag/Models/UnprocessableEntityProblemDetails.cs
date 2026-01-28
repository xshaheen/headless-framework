// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Models;

/// <summary>
/// Problem details schema for 422 Unprocessable Entity responses.
/// </summary>
public sealed class UnprocessableEntityProblemDetails : HeadlessProblemDetails
{
    public required Dictionary<string, List<ErrorDescriptor>> Errors { get; init; }
}
