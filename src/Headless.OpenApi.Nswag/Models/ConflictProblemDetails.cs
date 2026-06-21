// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Models;

/// <summary>
/// Problem details schema for 409 Conflict responses.
/// </summary>
public sealed class ConflictProblemDetails : HeadlessProblemDetails
{
    /// <summary>
    /// The list of business-rule violations that caused the conflict, each identified by a
    /// <c>g:snake_case</c> error code and a human-readable message.
    /// </summary>
    public required List<ErrorDescriptor> Errors { get; init; }
}
