// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>One consumer class as the emitter writes it: its model plus the dispatcher name chosen for the assembly.</summary>
internal sealed record ConsumerRegistrationModel(ConsumerModel Consumer, string DispatcherName);

/// <summary>The complete input of the emitter for one assembly. Contains no locations or diagnostics.</summary>
internal sealed record MessagingRegistrationModel(
    string AssemblyName,
    EquatableArray<ConsumerRegistrationModel> Consumers
);

/// <summary>
/// The combined outcome for one assembly. <see cref="Model"/> is <see langword="null"/> when nothing may be emitted,
/// such as when consumer identities collide.
/// </summary>
internal sealed record MessagingGenerationResult(
    MessagingRegistrationModel? Model,
    EquatableArray<DiagnosticInfo> Diagnostics
)
{
    public static MessagingGenerationResult Empty { get; } = new(Model: null, EquatableArray<DiagnosticInfo>.Empty);
}
