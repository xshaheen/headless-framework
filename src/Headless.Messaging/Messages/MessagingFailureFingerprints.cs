// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Headless.Messaging;

/// <summary>
/// The failure fingerprint a failed published or received row stores in <c>FailureFingerprint</c>, and the fixed
/// fingerprints for failures that have no thrown exception to compute one from.
/// </summary>
/// <remarks>
/// <para>
/// A fingerprint groups failures by cause, so a dashboard can show one item per cause instead of one per first
/// exception line, which often carries ids and values. When a send or a consumer throws, the core computes it from the
/// live exception with <see cref="FailureFingerprint.Compute"/>, the algorithm every Headless feature shares, and sets
/// <see cref="MediumMessage.FailureFingerprint"/> before the failure write, which stores it beside the exception text
/// in the same guarded statement.
/// </para>
/// <para>
/// The fixed values below cover the failures whose cause is the delivery rather than the code that ran: each equals
/// <see cref="FailureFingerprint.ForCause"/> of its cause name. A third-party storage provider writes
/// <see cref="Poison"/> for a row its claim cannot deserialize, and for a received failure stored on arrival without a
/// fingerprint, so every store groups those failures alike. They are constants so a provider can embed them in a
/// statement or an expression tree.
/// </para>
/// </remarks>
[PublicAPI]
public static class MessagingFailureFingerprints
{
    /// <summary>The prefix every version-1 fingerprint starts with.</summary>
    public const string VersionPrefix = FailureFingerprint.VersionPrefix;

    /// <summary>The longest fingerprint any version may store; the column's maximum length.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// A message whose deadline passed before it could be sent or consumed: a <c>DeliverWithin</c> deadline or a
    /// request's response deadline.
    /// </summary>
    public const string Expired = VersionPrefix + "expired";

    /// <summary>A received message whose payload could not be deserialized into the consumer's message type.</summary>
    public const string Deserialization = VersionPrefix + "deserialization";

    /// <summary>
    /// A message rejected before any consumer ran (poison on arrival: no registered subscriber at receive, a contract
    /// version mismatch, or a receive-middleware reject), or a stored row a claim could not deserialize.
    /// </summary>
    public const string Poison = VersionPrefix + "poison";

    /// <summary>
    /// The fingerprint of a send or consume failure: <see cref="Deserialization"/> when the failure's cause chain holds
    /// a <see cref="MessageDeserializationException"/>, else the fingerprint computed from the live exception inside
    /// the framework's send or consume wrapper, whose own type and frames every failure shares.
    /// </summary>
    internal static string Of(Exception exception)
    {
        return _IsDeserialization(exception)
            ? Deserialization
            : FailureFingerprint.Compute(MessagingTelemetry.FailureOf(exception));
    }

    /// <summary>
    /// The fingerprint of a message rejected on arrival: <see cref="Deserialization"/> for a payload that could not be
    /// deserialized, else <see cref="Poison"/>.
    /// </summary>
    internal static string OfArrival(Exception? exception)
    {
        return exception is not null && _IsDeserialization(exception) ? Deserialization : Poison;
    }

    private static bool _IsDeserialization(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MessageDeserializationException)
            {
                return true;
            }
        }

        return false;
    }
}
