// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using StackExchange.Redis;

namespace Headless.Caching;

internal static class RedisNativeGenerationKey
{
    private const int _SlotCount = 16_384;
    private const string _TagAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
    private static readonly string[] _SlotTags = _BuildSlotTags();

    // A synthetic hash tag colocates metadata even when the original key has unmatched or empty hash braces.
    // Starting with NUL keeps the metadata outside the namespace accepted by the cache's public key methods.
    public static RedisKey ForKey(RedisKey key, int slot)
    {
        var name = key.ToString();
        // StackExchange.Redis reports NoSlot when connected to a standalone primary. Keep the same layout
        // if that data is later moved to a cluster or called before its topology is known.
        if (slot < 0)
        {
            slot = _CalculateSlot(name);
        }

        return string.Concat("\0__native:{", _SlotTags[slot], "}:", name);
    }

    private static int _CalculateSlot(string key)
    {
        var start = key.IndexOf('{', StringComparison.Ordinal);
        var end = start < 0 ? -1 : key.IndexOf('}', start + 1);
        var tag = end > start + 1 ? key[(start + 1)..end] : key;
        var crc = 0;

        foreach (var value in Encoding.UTF8.GetBytes(tag))
        {
            crc = _AddByte(crc, value);
        }

        return crc % _SlotCount;
    }

    private static string[] _BuildSlotTags()
    {
        var tags = new string[_SlotCount];
        var remaining = _SlotCount;

        foreach (var first in _TagAlphabet)
        {
            var firstCrc = _AddByte(0, first);

            foreach (var second in _TagAlphabet)
            {
                var secondCrc = _AddByte(firstCrc, second);

                foreach (var third in _TagAlphabet)
                {
                    var slot = _AddByte(secondCrc, third) % _SlotCount;
                    if (tags[slot] is not null)
                    {
                        continue;
                    }

                    tags[slot] = string.Create(
                        3,
                        (first, second, third),
                        static (span, characters) =>
                        {
                            span[0] = characters.first;
                            span[1] = characters.second;
                            span[2] = characters.third;
                        }
                    );

                    if (--remaining == 0)
                    {
                        return tags;
                    }
                }
            }
        }

        throw new InvalidOperationException("Redis generation tags do not cover all hash slots.");
    }

    // Redis Cluster uses CRC-16/XMODEM modulo 16384 over the bytes between the key's hash-tag braces.
    private static int _AddByte(int crc, int value)
    {
        crc ^= value << 8;

        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 0x8000) == 0 ? crc << 1 : (crc << 1) ^ 0x1021;
        }

        return crc & 0xFFFF;
    }
}
