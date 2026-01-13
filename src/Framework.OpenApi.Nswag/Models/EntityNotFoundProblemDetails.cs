// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api.Models;

/// <summary>
/// Problem details schema for 404 Entity Not Found responses.
/// </summary>
/// <remarks>
/// Entity/key details are intentionally omitted to prevent information disclosure (OWASP A01:2021).
/// </remarks>
public sealed class EntityNotFoundProblemDetails : HeadlessProblemDetails;
