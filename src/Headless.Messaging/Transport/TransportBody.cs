// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging.Transport;

/// <summary>Hands a message body to a broker client that only accepts a byte array.</summary>
internal static class TransportBody
{
    /// <summary>
    /// Returns the body's backing array when the body spans the whole array, and a copy otherwise.
    /// </summary>
    /// <remarks>
    /// Reusing the array saves one body-sized copy per send. It is safe because the body is read-only for
    /// the message's lifetime and nothing in messaging rents it from a pool, so the broker client never sees
    /// the array change under it. A slice must be copied: the client would otherwise send the whole array.
    /// </remarks>
    public static byte[] ToArray(ReadOnlyMemory<byte> body)
    {
        if (
            MemoryMarshal.TryGetArray(body, out var segment)
            && segment is { Array: { } array, Offset: 0 }
            && segment.Count == array.Length
        )
        {
            return array;
        }

        return body.ToArray();
    }
}
