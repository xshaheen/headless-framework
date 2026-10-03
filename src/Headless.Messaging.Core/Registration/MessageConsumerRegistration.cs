// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;

namespace Headless.Messaging.Registration;

/// <summary>One attribute-declared consumer of one message, as its generated module declared it.</summary>
/// <param name="ConsumerType">The consumer class.</param>
/// <param name="Lane">The lane its attribute names.</param>
/// <param name="ConsumerIdentity">The consumer identity from its attribute.</param>
/// <param name="Dispatch">The generated dispatch that runs the consumer class.</param>
internal sealed record MessageConsumerRegistration(
    Type ConsumerType,
    MessageLane Lane,
    string ConsumerIdentity,
    MessageConsumerDispatch Dispatch
)
{
    /// <summary>Whether every process receives every message; only a Bus consumer sets it.</summary>
    public bool EveryInstance { get; init; }

    /// <summary>The generated module that declared the consumer, for conflict messages.</summary>
    public string? DeclaringModule { get; init; }

    /// <summary>The generated subscription hook of an every-instance consumer class, when it has one.</summary>
    public SubscriptionEstablishedDispatch? OnSubscriptionEstablished { get; init; }

    /// <summary>The response type of a responder; <see langword="null"/> for a consumer that does not answer requests.</summary>
    public Type? ResponseType { get; init; }

    /// <summary>Creates the failure policy the consumer's attribute declares, when it declares one.</summary>
    public Func<FailurePolicy>? FailurePolicyFactory { get; init; }
}
