// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Declares a Bus consumer that the Messaging source generator registers in the assembly's generated
/// <c>MessagingModule</c>. The consumer receives messages published on the Bus lane.
/// </summary>
/// <remarks>
/// Apply it to a non-abstract, non-generic <see langword="public"/> or <see langword="internal"/> class that implements
/// at least one <see cref="IConsume{TMessage}"/>; the consumer handles every message it implements the interface for. A
/// class carries one lane attribute: this one or <see cref="QueueConsumerAttribute"/>, not both.
/// <para>
/// On the Bus lane the identity is the broker subscription name. Consumers that share an identity compete for each
/// message in whatever processes register them, so moving a module to another process keeps its subscriptions. Set
/// <see cref="EveryInstance"/> for a consumer that must see every message in every process instead.
/// </para>
/// </remarks>
/// <param name="identity">
/// The durable consumer identity in <c>owner.name</c> form, for example <c>billing.invoice-projection</c>.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BusConsumerAttribute(string identity) : MessageConsumerAttribute(identity)
{
    /// <summary>
    /// Delivers every Bus message to every running process instead of to one competing consumer, for consumers that
    /// refresh per-process state such as an in-memory cache. Each process derives its own subscription from the
    /// identity.
    /// </summary>
    /// <remarks>
    /// Delivery is at most once and only while the process is subscribed: there is no backlog on start. A consumer that
    /// must resynchronize after a gap implements <see cref="IOnSubscriptionEstablished"/>. Only the Bus lane has this
    /// setting, because the Queue lane is point-to-point by definition.
    /// </remarks>
    public bool EveryInstance { get; set; }
}
