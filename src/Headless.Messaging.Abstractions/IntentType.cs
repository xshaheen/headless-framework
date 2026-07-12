// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Discriminator that identifies the delivery intent of a message: broadcast (<see cref="Bus"/>)
/// vs point-to-point (<see cref="Queue"/>).
/// </summary>
/// <remarks>
/// <para>
/// The intent flows end-to-end through the framework: publisher interface (<c>IBus</c> /
/// <c>IQueue</c> / <c>IOutboxBus</c> / <c>IOutboxQueue</c>) → outbox row →
/// drainer → transport interface (<c>IBusTransport</c> / <c>IQueueTransport</c>) →
/// <c>ConsumeContext&lt;TMessage&gt;.IntentType</c> → OpenTelemetry tag.
/// </para>
/// <para>
/// Backed by <see cref="short"/> (<c>SMALLINT</c>) so storage providers can persist it in a fixed-width
/// column. The underlying values are part of the on-disk contract — <see cref="Bus"/> is always <c>0</c>
/// and <see cref="Queue"/> is always <c>1</c>.
/// </para>
/// <para>
/// Additional members may be added in future versions, so consumers that switch on this enum should
/// include a default branch to handle values they do not recognize.
/// </para>
/// </remarks>
[PublicAPI]
#pragma warning disable CA1028 // Enum Storage should be Int32
public enum IntentType : short
#pragma warning restore CA1028
{
    /// <summary>
    /// Broadcast intent (publish/subscribe). Every subscriber receives its own copy of the message.
    /// </summary>
    Bus = 0,

    /// <summary>
    /// Point-to-point intent (work-queue). Exactly one competing worker receives each message.
    /// </summary>
    Queue = 1,
}
