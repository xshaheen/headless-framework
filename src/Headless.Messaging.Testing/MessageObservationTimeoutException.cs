// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;

namespace Headless.Messaging.Testing;

/// <summary>
/// Exception thrown when a <see cref="MessageObservationStore"/> wait operation times out.
/// </summary>
public sealed class MessageObservationTimeoutException : TimeoutException
{
    /// <summary>The message type that was expected.</summary>
    public Type ExpectedType { get; }

    /// <summary>The observation type that was waited on.</summary>
    public MessageObservationType ObservationType { get; }

    /// <summary>How long the wait lasted before timing out.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Messages that were recorded in the observed collection during the wait.</summary>
    public IReadOnlyList<RecordedMessage> ObservedMessages { get; }

    /// <summary>Whether a predicate filter was active during the wait.</summary>
    public bool HasPredicate { get; }

    internal MessageObservationTimeoutException(
        Type expectedType,
        MessageObservationType observationType,
        TimeSpan elapsed,
        IReadOnlyList<RecordedMessage> observedMessages,
        bool hasPredicate = false
    )
        : base(_BuildMessage(expectedType, observationType, elapsed, observedMessages, hasPredicate))
    {
        ExpectedType = expectedType;
        ObservationType = observationType;
        Elapsed = elapsed;
        ObservedMessages = observedMessages;
        HasPredicate = hasPredicate;
    }

    private static string _BuildMessage(
        Type expectedType,
        MessageObservationType observationType,
        TimeSpan elapsed,
        IReadOnlyList<RecordedMessage> observedMessages,
        bool hasPredicate
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Timed out after {elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s waiting for {observationType} of {expectedType.Name}."
        );

        var observationLabel = observationType.ToString().ToLowerInvariant();

        if (observedMessages.Count == 0)
        {
            sb.Append("No messages were ").Append(observationLabel).AppendLine(" during the wait.");
        }
        else
        {
            sb.Append("Messages ")
                .Append(observationLabel)
                .Append(" during the wait (")
                .Append(observedMessages.Count)
                .AppendLine("):");

            foreach (var msg in observedMessages.Take(10))
            {
                sb.Append("  - [")
                    .Append(msg.MessageType.Name)
                    .Append("] id=")
                    .Append(msg.MessageId)
                    .Append(" topic=")
                    .AppendLine(msg.Topic);
            }
        }

        if (hasPredicate)
        {
            sb.AppendLine(
                "Note: a predicate filter was active — the message type was observed but did not match the predicate."
            );
        }

        return sb.ToString();
    }
}
