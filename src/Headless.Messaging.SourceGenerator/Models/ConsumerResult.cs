// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>
/// The transform output for one consumer attribute application: the emission model plus what only validation needs,
/// kept apart so a location change alone never invalidates emitted source.
/// </summary>
/// <param name="Consumer">
/// The emission model, or <see langword="null"/> when an error already fails the build and no dispatcher could compile.
/// </param>
/// <param name="TypeName">The fully qualified class name, kept to detect a class that carries both lane attributes.</param>
/// <param name="Identity">The declared identity, kept for duplicate detection even when <paramref name="Consumer"/> is null.</param>
/// <param name="MessageTypeNames">
/// The consumed and the answered message types, kept to detect a second Queue consumer for one message: a responder is its
/// request's Queue consumer.
/// </param>
internal sealed record ConsumerResult(
    ConsumerModel? Consumer,
    ConsumerLane Lane,
    string TypeName,
    string? Identity,
    EquatableArray<string> MessageTypeNames,
    LocationInfo? AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics
);
