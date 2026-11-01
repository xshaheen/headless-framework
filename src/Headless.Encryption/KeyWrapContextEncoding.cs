// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Text;

namespace Headless.Encryption;

/// <summary>
/// The bytes a key-encryption-key provider binds a wrapped data key to: a provider label, then the data key's id
/// (big-endian), kind, and length-prefixed scope, so no two contexts encode the same.
/// </summary>
internal static class KeyWrapContextEncoding
{
    public static byte[] Encode(ReadOnlySpan<byte> label, KeyWrapContext context)
    {
        var scopeLength = Encoding.UTF8.GetByteCount(context.Scope);
        var bytes = new byte[label.Length + 16 + 1 + 4 + scopeLength];
        var offset = label.Length;

        label.CopyTo(bytes);
        context.KeyId.TryWriteBytes(bytes.AsSpan(offset, 16), bigEndian: true, out _);
        bytes[offset + 16] = (byte)context.Kind;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset + 17), scopeLength);
        Encoding.UTF8.GetBytes(context.Scope, bytes.AsSpan(offset + 21));

        return bytes;
    }
}
