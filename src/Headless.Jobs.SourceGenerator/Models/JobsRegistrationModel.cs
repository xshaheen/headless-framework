// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>The complete input of the emitter for one assembly. Contains no locations or diagnostics.</summary>
internal sealed record JobsRegistrationModel(
    string AssemblyName,
    EquatableArray<JobModel> Jobs,
    EquatableArray<MiddlewareRegistrationModel> Middleware
);
