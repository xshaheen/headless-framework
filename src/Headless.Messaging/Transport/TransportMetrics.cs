// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Transport;

/// <summary>
/// The measurements only a transport can take, recorded on the messaging meter the core owns, so a transport written
/// outside this repository reports them on the same instruments and tags as the built-in ones.
/// </summary>
/// <remarks>
/// The core records sends, receives, and handler outcomes itself. A transport records only what never reaches the core:
/// a settlement the broker did not get, and an every-instance message it discarded before handing it over. New
/// measurements arrive as new members; existing members never gain parameters.
/// </remarks>
public static class TransportMetrics
{
    /// <summary>The settlement operation name of a broker acknowledgement of a handled delivery.</summary>
    public const string OperationAck = MessagingMetrics.OperationNameAck;

    /// <summary>
    /// The error type of a settlement the transport could not send because the broker channel it belongs to had
    /// already closed.
    /// </summary>
    public const string ErrorTypeChannelClosed = MessagingMetrics.ErrorTypeChannelClosed;

    /// <summary>
    /// Records one settlement that did not reach the broker. The transport does not throw for it: the broker redelivers
    /// the message and the inbox deduplicates the redelivery, so this count is how an operator sees that settlement is
    /// failing.
    /// </summary>
    /// <param name="system">The transport's <c>messaging.system</c> value, such as <c>rabbitmq</c>.</param>
    /// <param name="operationName">The settlement operation, such as <see cref="OperationAck"/>.</param>
    /// <param name="errorType">Why it failed: <see cref="ErrorTypeChannelClosed"/> or <see cref="ErrorTypeOf"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is empty or white space.</exception>
    public static void RecordSettlementFailure(string system, string operationName, string errorType)
    {
        Argument.IsNotNullOrWhiteSpace(system);
        Argument.IsNotNullOrWhiteSpace(operationName);
        Argument.IsNotNullOrWhiteSpace(errorType);

        MessagingMetrics.RecordSettlementFailure(system, operationName, errorType);
    }

    /// <summary>
    /// Records one every-instance message the transport discarded before it reached the core, such as a local buffer
    /// overflow. It counts on the same instrument the core uses for every-instance outcomes, with outcome
    /// <c>dropped</c>.
    /// </summary>
    /// <param name="consumerIdentity">The Bus consumer identity the subscription belongs to.</param>
    /// <param name="errorType">A short, bounded reason, such as <c>overflow</c>; never a message identifier.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is empty or white space.</exception>
    public static void RecordEveryInstanceDrop(string consumerIdentity, string errorType)
    {
        Argument.IsNotNullOrWhiteSpace(consumerIdentity);
        Argument.IsNotNullOrWhiteSpace(errorType);

        MessagingMetrics.RecordEveryInstanceDelivery(consumerIdentity, "dropped", errorType);
    }

    /// <summary>
    /// Returns the <c>error.type</c> value the core records for <paramref name="exception"/>: the full type name of the
    /// failure, unwrapped from the framework's subscriber and publisher wrappers.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static string ErrorTypeOf(Exception exception)
    {
        Argument.IsNotNull(exception);

        return MessagingTelemetry.ErrorTypeOf(exception);
    }
}
