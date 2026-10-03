// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>Everything the emitter needs about one consumer class, captured as values.</summary>
/// <param name="TypeName">
/// The fully qualified (<c>global::</c>) class name, so generated code cannot be captured by the namespace it is emitted
/// into.
/// </param>
/// <param name="DisplayName">The namespace-qualified class name, used to name the class's dispatcher.</param>
/// <param name="Identity">The identity from the attribute.</param>
/// <param name="EveryInstance">Whether the Bus consumer sees every message in every process; always false on the Queue lane.</param>
/// <param name="MessageTypeNames">
/// The fully qualified <c>T</c> of every <c>IConsume&lt;T&gt;</c> the class implements, in ordinal order.
/// </param>
/// <param name="Responders">
/// Every <c>IRespond&lt;TRequest, TResponse&gt;</c> the class implements, in ordinal order of the request. Always empty on the
/// Bus lane, where a responder fails the build.
/// </param>
/// <param name="Disposal">How the dispatcher releases the instance it constructs.</param>
/// <param name="HasLifecycle">Whether the class implements <c>IConsumerLifecycle</c>, whose hooks run around each delivery.</param>
/// <param name="HasSubscriptionHook">
/// Whether the every-instance class implements <c>IOnSubscriptionEstablished</c>, so the module hands messaging a
/// generated call of it. Always false for any other consumer, where the hook never runs.
/// </param>
/// <param name="FailurePolicyTypeName">
/// The fully qualified (<c>global::</c>) name of the declared failure policy, which the module constructs through a
/// generated factory; null when the consumer declares none and the host default applies.
/// </param>
internal sealed record ConsumerModel(
    string TypeName,
    string DisplayName,
    ConsumerLane Lane,
    string Identity,
    bool EveryInstance,
    EquatableArray<string> MessageTypeNames,
    EquatableArray<ResponderModel> Responders,
    HandlerDisposal Disposal,
    bool HasLifecycle,
    bool HasSubscriptionHook,
    string? FailurePolicyTypeName
);
