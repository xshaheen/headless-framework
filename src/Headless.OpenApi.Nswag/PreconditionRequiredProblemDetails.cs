// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi.Nswag.Models;
using Headless.Primitives;

namespace Headless.OpenApi.Nswag;

/// <summary>Problem details schema for 428 Precondition Required responses.</summary>
public sealed class PreconditionRequiredProblemDetails : HeadlessProblemDetails
{
    /// <summary>The stable descriptor identifying the missing precondition.</summary>
    public required ErrorDescriptor Error { get; init; }
}
