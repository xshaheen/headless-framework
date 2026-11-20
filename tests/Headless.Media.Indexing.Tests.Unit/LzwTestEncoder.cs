// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Writes PDF LZW code streams for tests, packing codes at the widths PdfPig's decoder reads them.</summary>
internal static class LzwTestEncoder
{
    public const int ClearTable = 256;
    public const int EndOfData = 257;
    public const int FirstFreeCode = 258;

    /// <summary>Compresses <paramref name="data"/> with standard LZW into codes, without the clear and end codes.</summary>
    /// <remarks>The table stops growing at 4096 entries, the most a 12-bit code addresses.</remarks>
    public static List<int> Compress(ReadOnlySpan<byte> data)
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < 256; i++)
        {
            table[((char)i).ToString()] = i;
        }

        var codes = new List<int>();
        var next = FirstFreeCode;
        var current = string.Empty;

        foreach (var value in data)
        {
            var extended = current + (char)value;

            if (table.ContainsKey(extended))
            {
                current = extended;

                continue;
            }

            codes.Add(table[current]);

            if (next < 4096)
            {
                table[extended] = next++;
            }

            current = ((char)value).ToString();
        }

        if (current.Length > 0)
        {
            codes.Add(table[current]);
        }

        return codes;
    }

    /// <summary>
    /// Packs <paramref name="codes"/> most significant bit first, growing the width from 9 to 12 bits as the decoder's
    /// table grows: one code early when <paramref name="isEarlyChange"/> is <see langword="true"/>, as PDF's default.
    /// </summary>
    public static byte[] Pack(IEnumerable<int> codes, bool isEarlyChange = true)
    {
        var offset = isEarlyChange ? 0 : 1;
        var output = new List<byte>();
        var buffer = 0L;
        var bits = 0;
        var tableSize = FirstFreeCode;
        var hasPrevious = false;
        var width = 9;

        foreach (var code in codes)
        {
            buffer = (buffer << width) | (uint)code;
            bits += width;

            while (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
            }

            buffer &= (1L << bits) - 1;

            if (code == ClearTable)
            {
                tableSize = FirstFreeCode;
                hasPrevious = false;
                width = 9;

                continue;
            }

            if (code == EndOfData)
            {
                break;
            }

            // The decoder adds an entry for every code but the first after a clear.
            if (hasPrevious)
            {
                tableSize++;
            }

            hasPrevious = true;
            width =
                tableSize < 511 + offset ? 9
                : tableSize < 1023 + offset ? 10
                : tableSize < 2047 + offset ? 11
                : 12;
        }

        if (bits > 0)
        {
            output.Add((byte)(buffer << (8 - bits)));
        }

        return [.. output];
    }

    /// <summary>
    /// The codes of a stream that decodes to at least <paramref name="minDecodedBytes"/> copies of
    /// <paramref name="value"/>: each code grows the longest entry by one byte up to code 4095, then repeats it.
    /// </summary>
    public static List<int> Bomb(byte value, long minDecodedBytes)
    {
        List<int> codes = [ClearTable, value];
        long decoded = 1;

        // Each code equals the next free entry, which the decoder fills as the previous entry plus its first byte.
        for (var code = FirstFreeCode; code < 4096 && decoded < minDecodedBytes; code++)
        {
            codes.Add(code);
            decoded += code - FirstFreeCode + 2;
        }

        const int longest = 4095 - FirstFreeCode + 2;

        while (decoded < minDecodedBytes)
        {
            codes.Add(4095);
            decoded += longest;
        }

        codes.Add(EndOfData);

        return codes;
    }
}
