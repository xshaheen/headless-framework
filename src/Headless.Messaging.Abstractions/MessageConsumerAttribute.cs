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
    /// Failure policy type for this consumer. It must implement <c>Headless.Reliability.IFailurePolicy</c>; the source
    /// generator rejects any other type.
    /// </summary>
    /// <remarks>
    /// A policy set at the call site wins over this declaration, and this declaration wins over the host's default.
    /// Until a policy model is registered for the declared type, the consumer runs with the host's configured retry
    /// behavior.
    /// </remarks>
    public Type? Policy { get; set; }
}
