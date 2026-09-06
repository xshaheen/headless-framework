// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.OpenApi.Nswag.Models;

/// <summary>
/// Problem details schema for 404 Entity Not Found responses.
/// </summary>
/// <remarks>
/// Entity/key details are intentionally omitted to prevent information disclosure (OWASP A01:2021).
/// </remarks>
public sealed class EntityNotFoundProblemDetails : HeadlessProblemDetails
{
    /// <summary>
    /// The stable descriptor identifying what was not found, when one was supplied. It carries a code and
    /// description only — never the entity key, per the disclosure note above.
    /// </summary>
    public ErrorDescriptor? Error { get; init; }
}
