// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Declares a Queue consumer that the Messaging source generator registers in the assembly's generated
/// <c>MessagingModule</c>. The consumer receives messages sent point-to-point on the Queue lane.
/// </summary>
/// <remarks>
/// Apply it to a non-abstract, non-generic <see langword="public"/> or <see langword="internal"/> class that implements
/// at least one <see cref="IConsume{TMessage}"/>; the consumer handles every message it implements the interface for. A
/// class carries one lane attribute: this one or <see cref="BusConsumerAttribute"/>, not both.
/// <para>
/// A message has exactly one Queue consumer across every module a host registers, and its Queue destination stays keyed
/// by the message name, so a second Queue consumer for one message fails the build within an assembly and fails startup
/// across assemblies.
/// </para>
/// </remarks>
/// <param name="identity">
/// The durable consumer identity in <c>owner.name</c> form, for example <c>billing.issue-invoice</c>.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class QueueConsumerAttribute(string identity) : MessageConsumerAttribute(identity);
