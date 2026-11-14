// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Headless.Messaging.Internal;

/// <summary>Derives the stored key a sequential consumer orders its received rows under.</summary>
internal static class MessageSequenceKey
{
    /// <summary>
    /// The lowercase hex SHA-256 of the message's routing affinity key when <paramref name="consumer"/> runs
    /// sequentially, else <see langword="null"/>. A fixed 64-character value keeps the indexed column narrow whatever
    /// length the application's key has.
    /// </summary>
    public static string? For(ConsumerExecutorDescriptor? consumer, Message message)
    {
        if (consumer is not { Sequential: true })
        {
            return null;
        }

        return message.Headers.TryGetValue(Headers.RoutingAffinityKey, out var key) && key is not null
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
            : null;
    }
}
