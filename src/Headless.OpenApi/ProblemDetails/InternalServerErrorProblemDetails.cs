// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.OpenApi;

/// <summary>
/// Problem details schema for 500 Internal Server Error responses. The body never carries exception details.
/// </summary>
public sealed class InternalServerErrorProblemDetails : HeadlessProblemDetails;
