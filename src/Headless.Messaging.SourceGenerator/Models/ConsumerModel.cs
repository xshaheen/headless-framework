// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>The lane a consumer attribute names.</summary>
internal enum ConsumerLane
{
    Bus,
    Queue,
}

/// <summary>Everything the emitter needs about one consumer class, captured as values.</summary>
/// <param name="TypeName">
/// The fully qualified (<c>global::</c>) class name, so generated code cannot be captured by the namespace it is emitted
/// into.
/// </param>
/// <param name="DisplayName">The namespace-qualified class name, used to name the class's dispatcher.</param>
/// <param name="Identity">The identity from the attribute.</param>
/// <param name="EveryInstance">Whether the Bus consumer sees every message in every process; always false on the Queue lane.</param>
/// <param name="PolicyTypeName">The fully qualified failure policy type, or <see langword="null"/> when none is declared.</param>
/// <param name="MessageTypeNames">
/// The fully qualified <c>T</c> of every <c>IConsume&lt;T&gt;</c> the class implements, in ordinal order.
/// </param>
/// <param name="Disposal">How the dispatcher releases the instance it constructs.</param>
/// <param name="HasLifecycle">Whether the class implements <c>IConsumerLifecycle</c>, whose hooks run around each delivery.</param>
internal sealed record ConsumerModel(
    string TypeName,
    string DisplayName,
    ConsumerLane Lane,
    string Identity,
    bool EveryInstance,
    string? PolicyTypeName,
    EquatableArray<string> MessageTypeNames,
    HandlerDisposal Disposal,
    bool HasLifecycle
);

/// <summary>
/// The transform output for one consumer attribute application: the emission model plus what only validation needs,
/// kept apart so a location change alone never invalidates emitted source.
/// </summary>
/// <param name="Consumer">
/// The emission model, or <see langword="null"/> when an error already fails the build and no dispatcher could compile.
/// </param>
/// <param name="TypeName">The fully qualified class name, kept to detect a class that carries both lane attributes.</param>
/// <param name="Identity">The declared identity, kept for duplicate detection even when <paramref name="Consumer"/> is null.</param>
/// <param name="MessageTypeNames">The consumed message types, kept to detect a second Queue consumer for one message.</param>
internal sealed record ConsumerResult(
    ConsumerModel? Consumer,
    ConsumerLane Lane,
    string TypeName,
    string? Identity,
    EquatableArray<string> MessageTypeNames,
    LocationInfo? AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics
);
