// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.DistributedLocks.Postgres;

/// <summary>PostgreSQL advisory-lock key in either the bigint or the two-int key space.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly struct PostgresAdvisoryLockKey : IEquatable<PostgresAdvisoryLockKey>
{
    private const int _AsciiCharBits = 7;
    private const int _MaxAsciiValue = (1 << _AsciiCharBits) - 1;
    private const char _HashStringSeparator = ',';
    private const int _HashPartLength = 8;
    private const int _HashStringLength = 16;
    private const int _SeparatedHashStringLength = _HashStringLength + 1;

    private readonly long _key;
    private readonly KeyEncoding _keyEncoding;

    public PostgresAdvisoryLockKey(long key)
    {
        _key = key;
        _keyEncoding = KeyEncoding.Int64;
    }

    public PostgresAdvisoryLockKey(int key1, int key2)
    {
        _key = _CombineKeys(key1, key2);
        _keyEncoding = KeyEncoding.Int32Pair;
    }

    public static PostgresAdvisoryLockKey FromString(string name, bool allowHashing = true)
    {
        Argument.IsNotNull(name);

        if (_TryEncodeAscii(name, out var key))
        {
            return new PostgresAdvisoryLockKey(key, KeyEncoding.Ascii);
        }

        if (_TryEncodeHashString(name, out key, out var hasSeparator))
        {
            return new PostgresAdvisoryLockKey(key, hasSeparator ? KeyEncoding.Int32Pair : KeyEncoding.Int64);
        }

        if (allowHashing)
        {
            return new PostgresAdvisoryLockKey(_HashString(name));
        }

        throw new FormatException($"Name '{name}' could not be encoded as a {nameof(PostgresAdvisoryLockKey)}.");
    }

    private PostgresAdvisoryLockKey(long key, KeyEncoding encoding)
    {
        _key = key;
        _keyEncoding = encoding;
    }

    public bool HasSingleKey => _keyEncoding is KeyEncoding.Int64 or KeyEncoding.Ascii;

    public long Key => HasSingleKey ? _key : throw new InvalidOperationException("This advisory key uses two int keys.");

    public (int Key1, int Key2) Keys => _SplitKeys(_key);

    public bool Equals(PostgresAdvisoryLockKey other) => (_key, HasSingleKey).Equals((other._key, other.HasSingleKey));

    public override bool Equals(object? obj) => obj is PostgresAdvisoryLockKey other && Equals(other);

    public override int GetHashCode() => (_key, HasSingleKey).GetHashCode();

    public override string ToString()
    {
        return _keyEncoding switch
        {
            KeyEncoding.Int64 => _ToHashString(_key),
            KeyEncoding.Int32Pair => _ToHashString(_SplitKeys(_key)),
            KeyEncoding.Ascii => _ToAsciiString(_key),
            _ => string.Empty,
        };
    }

    public static bool operator ==(PostgresAdvisoryLockKey left, PostgresAdvisoryLockKey right) => left.Equals(right);

    public static bool operator !=(PostgresAdvisoryLockKey left, PostgresAdvisoryLockKey right) => !left.Equals(right);

    private static long _CombineKeys(int key1, int key2)
    {
        return unchecked(((long)key1 << (8 * sizeof(int))) | (uint)key2);
    }

    private static (int Key1, int Key2) _SplitKeys(long key)
    {
        return ((int)(key >> (8 * sizeof(int))), unchecked((int)(key & uint.MaxValue)));
    }

    private static bool _TryEncodeAscii(string name, out long key)
    {
        if (name.Length > (8 * sizeof(long)) / _AsciiCharBits)
        {
            key = default;
            return false;
        }

        var result = 0L;

        foreach (var character in name)
        {
            if (character > _MaxAsciiValue)
            {
                key = default;
                return false;
            }

            result = (result << _AsciiCharBits) | character;
        }

        result <<= 1;

        for (var i = name.Length; i < (8 * sizeof(long)) / _AsciiCharBits; i++)
        {
            result = (result << _AsciiCharBits) | _MaxAsciiValue;
        }

        key = result;

        return true;
    }

    private static string _ToAsciiString(long key)
    {
        var remainingKeyBits = unchecked((ulong)key);
        var length = (8 * sizeof(long)) / _AsciiCharBits;

        while ((remainingKeyBits & _MaxAsciiValue) == _MaxAsciiValue)
        {
            length--;
            remainingKeyBits >>= _AsciiCharBits;
        }

        remainingKeyBits >>= 1;
        var chars = new char[length];

        for (var i = length - 1; i >= 0; i--)
        {
            chars[i] = (char)(remainingKeyBits & _MaxAsciiValue);
            remainingKeyBits >>= _AsciiCharBits;
        }

        return new string(chars);
    }

    private static bool _TryEncodeHashString(string name, out long key, out bool hasSeparator)
    {
        hasSeparator = name.Length == _SeparatedHashStringLength && name[_HashPartLength] == _HashStringSeparator;

        if (!hasSeparator && name.Length != _HashStringLength)
        {
            key = default;
            return false;
        }

        if (
            int.TryParse(
                name[.._HashPartLength],
                NumberStyles.AllowHexSpecifier,
                NumberFormatInfo.InvariantInfo,
                out var key1
            )
            && int.TryParse(
                name[^_HashPartLength..],
                NumberStyles.AllowHexSpecifier,
                NumberFormatInfo.InvariantInfo,
                out var key2
            )
        )
        {
            key = _CombineKeys(key1, key2);
            return true;
        }

        key = default;
        return false;
    }

    private static long _HashString(string name)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var result = 0L;

        for (var i = sizeof(long) - 1; i >= 0; i--)
        {
            result = (result << 8) | hashBytes[i];
        }

        return result;
    }

    private static string _ToHashString(long key)
    {
        return _ToHashString(_SplitKeys(key)).Replace(",", "", StringComparison.Ordinal);
    }

    private static string _ToHashString((int Key1, int Key2) keys)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{keys.Key1:x8},{keys.Key2:x8}"
        );
    }

    private enum KeyEncoding
    {
        Int64,
        Int32Pair,
        Ascii,
    }
}
