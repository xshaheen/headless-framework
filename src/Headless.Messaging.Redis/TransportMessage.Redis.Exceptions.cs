// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message headers field.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
[PublicAPI]
public sealed class RedisConsumeMissingHeadersException(string entryId)
    : Exception($"Redis entry [{entryId}] is missing message headers.");

/// <summary>
/// Thrown when a Redis stream entry is consumed but does not contain the expected message body field.
/// </summary>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
[PublicAPI]
public sealed class RedisConsumeMissingBodyException(string entryId)
    : Exception($"Redis entry [{entryId}] is missing message body.");

/// <summary>
/// Thrown when a Redis stream entry's headers field exists but cannot be deserialized into the
/// expected message header format, or when the deserialized headers do not form a valid Messaging envelope.
/// </summary>
/// <remarks>
/// An envelope rejection carries an <see cref="InvalidDataException"/> whose message says what is wrong, such as a
/// missing message id, a message name that conflicts with the stream's contract, or an over-long message id. The
/// error context passed to <see cref="RedisMessagingOptions.OnConsumeError"/> keeps that reason and replaces a
/// deserialization failure with a generic one, because a serializer message can quote the raw headers.
/// </remarks>
/// <param name="entryId">The Redis stream entry identifier the parse failure originated from.</param>
/// <param name="ex">The underlying deserialization failure or envelope rejection.</param>
[PublicAPI]
public sealed class RedisConsumeInvalidHeadersException(string entryId, Exception ex)
    : Exception($"Redis entry [{entryId}] has not headers that are formatted properly as message headers.", ex);
