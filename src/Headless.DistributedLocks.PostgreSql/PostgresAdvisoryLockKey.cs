// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Headless.Checks;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Represents a PostgreSQL advisory-lock key in either the bigint (<c>int8</c>) or the two-int
/// (<c>int4, int4</c>) key space, and handles the mapping from arbitrary resource name strings into
/// those key spaces.
/// </summary>
/// <remarks>
/// <para>
/// Key encoding is chosen in priority order:
/// <list type="number">
///   <item><description>
///     <b>ASCII</b> — names whose characters are all 7-bit ASCII and fit within 9 characters are
///     packed losslessly into a single <see cref="long"/> using 7 bits per character.
///   </description></item>
///   <item><description>
///     <b>Hash-string</b> — names that look like the hex-encoded output of <see cref="ToString"/>
///     (16 hex characters, or 8+<c>,</c>+8) are decoded directly without hashing.
///   </description></item>
///   <item><description>
///     <b>SHA-256 hash</b> — names that do not fit either scheme are SHA-256-hashed and the first
///     8 bytes of the digest form a <see cref="long"/> key.  Results are memoized in a
///     process-scoped <see cref="ConcurrentDictionary{TKey,TValue}"/> so the retry loop does not
///     re-hash the same resource name on every poll.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The <c>pg_locks</c> view splits a single bigint key into <c>(classid, objid, objsubid=1)</c>
/// and a two-int pair into <c>(classid=key1, objid=key2, objsubid=2)</c>.
/// <see cref="AddLockFilter(NpgsqlCommand)"/> encodes the correct predicate for both forms so
/// queries that inspect <c>pg_locks</c> do not conflate the two key shapes.
/// </para>
/// <para>
/// This is a <see langword="readonly"/> value type. Equality is defined over (<c>_key</c>,
/// <see cref="HasSingleKey"/>): two keys are equal if and only if both their packed value and their
/// key-space variant are the same.
/// </para>
/// </remarks>
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
    private const int _MaxHashedKeyCacheEntries = 4096;
    private const int _MaxStackallocUtf8Bytes = 512;

    // Memoizes the SHA256-hashed keys for long names so the provider's retry loop (one FromString per
    // poll) does not re-hash the same resource string every attempt. The soft cap avoids retaining
    // unbounded high-cardinality resource names for the process lifetime.
    private static readonly ConcurrentDictionary<string, PostgresAdvisoryLockKey> _HashedKeyCache = new(
        StringComparer.Ordinal
    );

    private readonly long _key;
    private readonly KeyEncoding _keyEncoding;

    /// <summary>Initializes a key in the bigint (<c>int8</c>) advisory-lock key space.</summary>
    /// <param name="key">The 64-bit advisory-lock key value.</param>
    public PostgresAdvisoryLockKey(long key)
    {
        _key = key;
        _keyEncoding = KeyEncoding.Int64;
    }

    /// <summary>Initializes a key in the two-int (<c>int4, int4</c>) advisory-lock key space.</summary>
    /// <param name="key1">The first 32-bit component of the advisory-lock key pair.</param>
    /// <param name="key2">The second 32-bit component of the advisory-lock key pair.</param>
    public PostgresAdvisoryLockKey(int key1, int key2)
    {
        _key = _CombineKeys(key1, key2);
        _keyEncoding = KeyEncoding.Int32Pair;
    }

    /// <summary>
    /// Derives a <see cref="PostgresAdvisoryLockKey"/> from an arbitrary resource name string using the
    /// encoding priority described on the type: ASCII packing → hash-string passthrough → SHA-256 hash.
    /// </summary>
    /// <param name="name">The resource name to encode. Must not be <see langword="null"/>.</param>
    /// <param name="allowHashing">
    /// When <see langword="true"/> (the default), names that cannot be encoded losslessly are SHA-256-hashed.
    /// When <see langword="false"/>, a name that does not fit ASCII or hash-string encoding throws
    /// <see cref="FormatException"/>.
    /// </param>
    /// <returns>The derived <see cref="PostgresAdvisoryLockKey"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="allowHashing"/> is <see langword="false"/> and <paramref name="name"/>
    /// cannot be encoded as ASCII or a hash-string key.
    /// </exception>
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
            return _GetOrHashString(name);
        }

        throw new FormatException($"Name '{name}' could not be encoded as a {nameof(PostgresAdvisoryLockKey)}.");
    }

    private PostgresAdvisoryLockKey(long key, KeyEncoding encoding)
    {
        _key = key;
        _keyEncoding = encoding;
    }

    /// <summary>
    /// Gets a value indicating whether this key occupies the bigint key space (i.e. uses a single
    /// <see cref="long"/> key). Returns <see langword="false"/> when the key uses the two-int pair space.
    /// </summary>
    public bool HasSingleKey => _keyEncoding is KeyEncoding.Int64 or KeyEncoding.Ascii;

    /// <summary>
    /// Gets the 64-bit key value for use with the single-key advisory-lock functions
    /// (<c>pg_advisory_lock(bigint)</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="HasSingleKey"/> is <see langword="false"/>; use <see cref="Keys"/> instead.
    /// </exception>
    public long Key =>
        HasSingleKey ? _key : throw new InvalidOperationException("This advisory key uses two int keys.");

    /// <summary>
    /// Gets the two-int key pair for use with the two-argument advisory-lock functions
    /// (<c>pg_advisory_lock(int, int)</c>). Valid for all key encodings: single-key values are split
    /// into their high and low 32-bit halves.
    /// </summary>
    public (int Key1, int Key2) Keys => _SplitKeys(_key);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="other"/> has the same packed key value and
    /// occupies the same key space (<see cref="HasSingleKey"/>).
    /// </summary>
    /// <param name="other">The key to compare against.</param>
    public bool Equals(PostgresAdvisoryLockKey other)
    {
        return (_key, HasSingleKey).Equals((other._key, other.HasSingleKey));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is PostgresAdvisoryLockKey other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(_key, HasSingleKey);
    }

    /// <summary>
    /// Returns the canonical hex-string representation of this key: 16 hex characters for a bigint key,
    /// or <c>xxxxxxxx,yyyyyyyy</c> (8 + comma + 8) for a two-int pair. The output is accepted by
    /// <see cref="FromString"/> as a hash-string passthrough.
    /// </summary>
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

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    public static bool operator ==(PostgresAdvisoryLockKey left, PostgresAdvisoryLockKey right) => left.Equals(right);

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are not equal.</summary>
    public static bool operator !=(PostgresAdvisoryLockKey left, PostgresAdvisoryLockKey right) => !left.Equals(right);

    // Advisory-key SQL helpers shared by every command-emitting call site (the transaction API on
    // NpgsqlCommand, and the multiplexing engine on DatabaseCommand). Co-located with the key encoding
    // so the parameter binding and the (classid, objid, objsubid) pg_locks split stay in one place.

    /// <summary>
    /// Binds this key's parameter(s) to <paramref name="command"/> and returns the SQL placeholder list
    /// for an advisory-lock function call (for example <c>@key</c> or <c>@key1, @key2</c>).
    /// </summary>
    internal string AddKeyParameters(NpgsqlCommand command)
    {
        if (HasSingleKey)
        {
            command.Parameters.AddWithValue("key", Key);

            return "@key";
        }

        var (key1, key2) = Keys;
        command.Parameters.AddWithValue("key1", key1);
        command.Parameters.AddWithValue("key2", key2);

        return "@key1, @key2";
    }

    /// <inheritdoc cref="AddKeyParameters(NpgsqlCommand)"/>
    internal string AddKeyParameters(DatabaseCommand command)
    {
        if (HasSingleKey)
        {
            command.AddParameter("key", Key, DbType.Int64);

            return "@key";
        }

        var (key1, key2) = Keys;
        command.AddParameter("key1", key1, DbType.Int32);
        command.AddParameter("key2", key2, DbType.Int32);

        return "@key1, @key2";
    }

    /// <summary>
    /// Binds this key's <c>pg_locks</c> filter parameters to <paramref name="command"/> and returns the
    /// SQL predicate matching its (classid, objid, objsubid) split.
    /// </summary>
    /// <remarks>
    /// pg_locks splits a bigint advisory key into classid (high 32 bits) / objid (low 32 bits) with
    /// objsubid = 1; an (int,int) key uses classid = key1, objid = key2, objsubid = 2. Filtering on
    /// objsubid prevents conflating a single-bigint key with an (int,int) key whose halves coincide.
    /// </remarks>
    internal string AddLockFilter(NpgsqlCommand command)
    {
        var (key1, key2) = Keys;
        command.Parameters.AddWithValue("classId", key1);
        command.Parameters.AddWithValue("objId", key2);
        command.Parameters.AddWithValue("objSubId", (short)(HasSingleKey ? 1 : 2));

        return "l.classid = @classId AND l.objid = @objId AND l.objsubid = @objSubId";
    }

    /// <inheritdoc cref="AddLockFilter(NpgsqlCommand)"/>
    internal string AddLockFilter(DatabaseCommand command)
    {
        var (key1, key2) = Keys;
        command.AddParameter("classId", key1, DbType.Int32);
        command.AddParameter("objId", key2, DbType.Int32);
        command.AddParameter("objSubId", (short)(HasSingleKey ? 1 : 2), DbType.Int16);

        return "l.classid = @classId AND l.objid = @objId AND l.objsubid = @objSubId";
    }

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
        if (name.Length > 8 * sizeof(long) / _AsciiCharBits)
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

        for (var i = name.Length; i < 8 * sizeof(long) / _AsciiCharBits; i++)
        {
            result = (result << _AsciiCharBits) | _MaxAsciiValue;
        }

        key = result;

        return true;
    }

    private static string _ToAsciiString(long key)
    {
        var remainingKeyBits = unchecked((ulong)key);
        var length = 8 * sizeof(long) / _AsciiCharBits;

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
        var byteCount = Encoding.UTF8.GetByteCount(name);
        byte[]? rentedBytes = null;
        Span<byte> utf8Bytes =
            byteCount <= _MaxStackallocUtf8Bytes
                ? stackalloc byte[byteCount]
                : rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var written = Encoding.UTF8.GetBytes(name, utf8Bytes);
            Span<byte> hashBytes = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(utf8Bytes[..written], hashBytes);

            return BinaryPrimitives.ReadInt64LittleEndian(hashBytes);
        }
        finally
        {
            if (rentedBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }

    private static PostgresAdvisoryLockKey _GetOrHashString(string name)
    {
        if (_HashedKeyCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var hashed = new PostgresAdvisoryLockKey(_HashString(name));

        if (_HashedKeyCache.Count >= _MaxHashedKeyCacheEntries)
        {
            return hashed;
        }

        return _HashedKeyCache.GetOrAdd(name, hashed);
    }

    private static string _ToHashString(long key)
    {
        return _ToHashString(_SplitKeys(key)).Replace(",", "", StringComparison.Ordinal);
    }

    private static string _ToHashString((int Key1, int Key2) keys)
    {
        return $"{keys.Key1:x8},{keys.Key2:x8}";
    }

    private enum KeyEncoding
    {
        Int64,
        Int32Pair,
        Ascii,
    }
}
