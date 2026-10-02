// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// The shared shape of the two consumer lane attributes, <see cref="BusConsumerAttribute"/> and
/// <see cref="QueueConsumerAttribute"/>, so tooling reads a consumer declaration the same way whichever lane it names.
/// </summary>
/// <remarks>
/// Only the two lane attributes derive from it: its constructor is not accessible outside this assembly, so a consumer
/// class always names its lane explicitly. The messages a consumer handles are exactly the <see cref="IConsume{TMessage}"/>
/// interfaces its class implements.
/// </remarks>
[PublicAPI]
public abstract class MessageConsumerAttribute : Attribute
{
    private protected MessageConsumerAttribute(string identity)
    {
        Identity = identity;
    }

    /// <summary>
    /// The durable consumer identity in <c>owner.name</c> form, for example <c>billing.invoice-projection</c>. The first
    /// segment names the owning module or service, and the identity is at most 200 characters.
    /// </summary>
    /// <remarks>
    /// The identity keys the consumer's durable inbox state and its host tuning, so renaming it starts a new consumer
    /// rather than moving the old one.
    /// </remarks>
    public string Identity { get; }

    /// <summary>
    /// The failure policy of this consumer: a type derived from <see cref="Headless.Reliability.FailurePolicy"/> with a
    /// public parameterless constructor, for example <c>FailurePolicy = typeof(PaymentsFailurePolicy)</c>. When unset,
    /// the consumer uses the host's default failure policy.
    /// </summary>
    /// <remarks>
    /// The Messaging source generator reads this property and validates the type at build time, so the runtime never
    /// creates the policy by reflection. Host tuning and configuration can still replace or adjust the declared policy.
    /// An every-instance <see cref="BusConsumerAttribute"/> consumer cannot declare one: its deliveries are at most
    /// once and never stored, so there is nothing to retry.
    /// </remarks>
    public Type? FailurePolicy { get; set; }
}
