// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Threading;

namespace Headless.Messaging.Internal;

/// <summary>
/// How long a transport holds back a rejected delivery before it is delivered again. The core rejects every delivery
/// while a consumer's circuit is open or a half-open probe is out, so a transport that redelivered at once would spin on
/// the same messages; transports whose broker takes a redelivery delay pass this one, and transports that requeue
/// themselves wait it.
/// </summary>
internal static class RejectRedeliveryDelay
{
    /// <summary>
    /// About one second for the first delivery, doubling with each redelivery up to 30 seconds, and jittered so the
    /// deliveries rejected by one open circuit do not return together.
    /// </summary>
    /// <param name="deliveryCount">How many times the message has been delivered, this delivery included.</param>
    /// <param name="random">The jitter source.</param>
    internal static TimeSpan For(ulong deliveryCount, Random random)
    {
        // Five doublings from one second already pass the 30 s ceiling.
        var doublings = (int)Math.Min(deliveryCount <= 1 ? 0 : deliveryCount - 1, 5);
        var nominal = TimeSpan.FromTicks(ReconnectBackoff.FirstDelay.Ticks << doublings);

        return ReconnectBackoff.Jitter(nominal, TimeSpan.Zero, random);
    }
}
