// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 422 Unprocessable Entity responses.
/// </summary>
public sealed class UnprocessableEntityProblemDetails : HeadlessProblemDetails
{
    /// <summary>
    /// A dictionary of field-level validation failures keyed by the property name (typically camelCase).
    /// Each value is the ordered list of <c>ErrorDescriptor</c> entries produced for that field.
    /// </summary>
    public required Dictionary<string, List<ErrorDescriptor>> Errors { get; init; }
}
