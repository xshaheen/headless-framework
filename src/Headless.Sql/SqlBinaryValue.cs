// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>Hands a binary parameter value to a driver that binds only a whole <see cref="byte" /> array.</summary>
internal static class SqlBinaryValue
{
    /// <summary>
    /// Returns the backing array when <paramref name="value" /> spans the whole array, and a copy otherwise.
    /// </summary>
    /// <remarks>
    /// Reusing the array saves one value-sized copy per bind, which for a stored response body is the largest
    /// allocation of the write. The driver reads the array when the command runs, so the caller must leave it
    /// unchanged until then; a slice must be copied, because the driver would otherwise send the whole array.
    /// </remarks>
    public static byte[] ToArray(ReadOnlyMemory<byte> value)
    {
        if (
            MemoryMarshal.TryGetArray(value, out var segment)
            && segment is { Array: { } array, Offset: 0 }
            && segment.Count == array.Length
        )
        {
            return array;
        }

        return value.ToArray();
    }
}
