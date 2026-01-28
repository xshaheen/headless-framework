// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Models;

/// <summary>
/// Problem details schema for 409 Conflict responses.
/// </summary>
public sealed class ConflictProblemDetails : HeadlessProblemDetails
{
    public required List<ErrorDescriptor> Errors { get; init; }
}
