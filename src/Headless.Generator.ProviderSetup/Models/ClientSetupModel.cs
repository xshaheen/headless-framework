// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Generator.ProviderSetup.Models;

/// <summary>Everything the emitter needs about one <c>[GenerateClientSetup]</c> options class, captured as values.</summary>
internal sealed record ClientSetupModel(
    string OptionsTypeName,
    string OptionsTypeNamespace,
    string SetupNamespace,
    string ValidatorTypeName,
    string AddMethodName,
    string HttpClientName,
    string Effect,
    string? IdempotencyHeader
);

/// <summary>The transform output for one <c>[GenerateClientSetup]</c> options class.</summary>
internal sealed record ClientSetupResult(ClientSetupModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);
