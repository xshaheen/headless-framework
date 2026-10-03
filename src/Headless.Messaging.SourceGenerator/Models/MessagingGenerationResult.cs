// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

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
