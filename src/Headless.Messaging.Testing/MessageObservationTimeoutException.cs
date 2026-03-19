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

    internal MessageObservationTimeoutException(
        Type expectedType,
        MessageObservationType observationType,
        TimeSpan elapsed,
        IReadOnlyList<RecordedMessage> observedMessages
    )
        : base(_BuildMessage(expectedType, observationType, elapsed, observedMessages))
    {
        ExpectedType = expectedType;
        ObservationType = observationType;
        Elapsed = elapsed;
        ObservedMessages = observedMessages;
    }

    private static string _BuildMessage(
        Type expectedType,
        MessageObservationType observationType,
        TimeSpan elapsed,
        IReadOnlyList<RecordedMessage> observedMessages
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "Timed out after {0:F1}s waiting for {1} of {2}.",
                elapsed.TotalSeconds,
                observationType,
                expectedType.Name
            )
        );

        var observationLabel = observationType.ToString().ToLowerInvariant();

        if (observedMessages.Count == 0)
        {
            sb.AppendLine($"No messages were {observationLabel} during the wait.");
        }
        else
        {
            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Messages {0} during the wait ({1}):",
                    observationLabel,
                    observedMessages.Count
                )
            );

            foreach (var msg in observedMessages.Take(10))
            {
                sb.AppendLine($"  - [{msg.MessageType.Name}] id={msg.MessageId} topic={msg.Topic}");
            }
        }

        return sb.ToString();
    }
}
