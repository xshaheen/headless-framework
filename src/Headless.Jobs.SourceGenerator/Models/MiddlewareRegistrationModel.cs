// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>A resolved, de-duplicated middleware registration.</summary>
internal sealed record MiddlewareRegistrationModel(
    string TypeName,
    string Identity,
    string? Function,
    int Priority,
    bool IsSchedule
);
