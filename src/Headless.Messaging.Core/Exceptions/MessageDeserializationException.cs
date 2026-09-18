// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Exceptions;

/// <summary>
/// Thrown when an inbound message payload cannot be deserialized into the consumer's declared
/// contract, whether detected on arrival or during dispatch of a persisted retry.
/// </summary>
/// <remarks>
/// A payload defect is deterministic: redelivery reproduces the same failure. This exception is
/// therefore terminal — the delivery is routed to the exhausted path (poison storage on arrival,
/// a failed row with no retries during dispatch) instead of burning the retry budget. It wraps the
/// underlying serializer exception when one exists.
/// </remarks>
[PublicAPI]
public sealed class MessageDeserializationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
