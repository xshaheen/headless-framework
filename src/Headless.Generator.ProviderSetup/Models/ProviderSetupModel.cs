// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Generator.ProviderSetup.Models;

/// <summary>
/// Everything the emitter needs about one <c>[GenerateProviderSetup]</c> options class, captured as values so every
/// incremental step compares by content.
/// </summary>
internal sealed record ProviderSetupModel(
    string OptionsTypeName,
    string OptionsTypeNamespace,
    string ValidatorTypeName,
    string SenderTypeName,
    string UseMethodName,
    string HttpClientName,
    string Effect,
    string? IdempotencyHeader,
    EquatableArray<SenderParameterModel> SenderParameters,
    bool RegistersBulkForward
);

/// <summary>
/// One sender-constructor parameter, already classified into how the generated factory supplies it. The closed
/// convention every templated provider's sender follows: <see cref="SenderParameterKind.HttpClientFactory"/>, the
/// <see cref="SenderParameterKind.HttpClientNameString"/>, <see cref="SenderParameterKind.OptionsMonitor"/>, the
/// <see cref="SenderParameterKind.OptionsNameString"/>, then any of <see cref="SenderParameterKind.Logger"/> /
/// <see cref="SenderParameterKind.TimeProvider"/> in any order.
/// </summary>
internal sealed record SenderParameterModel(SenderParameterKind Kind);

internal enum SenderParameterKind
{
    HttpClientFactory,
    HttpClientNameString,
    OptionsMonitor,
    OptionsNameString,
    Logger,
    TimeProvider,
}

/// <summary>The transform output for one attributed options class: emission model plus validation-only data.</summary>
internal sealed record ProviderSetupResult(
    ProviderSetupModel? Model,
    LocationInfo? AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics
);
