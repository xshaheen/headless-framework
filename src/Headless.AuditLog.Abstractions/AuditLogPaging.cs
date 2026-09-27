// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Buffers.Text;
using Headless.Checks;

namespace Headless.AuditLog;

/// <summary>
/// Validates <see cref="AuditLogQuery"/> paging input and encodes the keyset position <c>(CreatedAt, Id)</c> of a
/// page's last entry as the opaque continuation token every audit-log store emits and accepts, so every provider
/// rejects the same input and a token keeps one meaning across providers.
/// </summary>
internal static class AuditLogPaging
{
    // A leading version byte lets a later layout change reject old tokens instead of misreading them.
    private const byte _Version = 1;
    private const int _Length = 1 + sizeof(long) + sizeof(long);
    private const string _InvalidTokenMessage = "The audit log continuation token is not valid.";

    /// <summary>Validates <paramref name="query"/> and returns the position to resume after, if any.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page size is less than one or equal to int.MaxValue.</exception>
    /// <exception cref="ArgumentException">The direction is undefined or the continuation token is malformed.</exception>
    public static (DateTime CreatedAtUtc, long Id)? Validate(AuditLogQuery query)
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(query.Size, "The query page size must be positive.", nameof(query));
        // Size + 1 (the one-extra-row look-ahead every store takes) must not overflow.
        Argument.IsLessThan(
            query.Size,
            int.MaxValue,
            "The query page size must be less than int.MaxValue.",
            nameof(query)
        );
        Argument.IsInEnum(query.Direction, paramName: nameof(query));

        return query.ContinuationToken is null ? null : _Decode(query.ContinuationToken, nameof(query));
    }

    public static string Encode(DateTime createdAtUtc, long id)
    {
        Span<byte> buffer = stackalloc byte[_Length];
        buffer[0] = _Version;
        BinaryPrimitives.WriteInt64BigEndian(buffer[1..], createdAtUtc.Ticks);
        BinaryPrimitives.WriteInt64BigEndian(buffer[(1 + sizeof(long))..], id);

        return Base64Url.EncodeToString(buffer);
    }

    private static (DateTime CreatedAtUtc, long Id) _Decode(string token, string paramName)
    {
        // One spare byte makes an over-long payload decode past _Length and fail the length check below.
        Span<byte> buffer = stackalloc byte[_Length + 1];

        if (
            token.Length > Base64Url.GetEncodedLength(_Length)
            // TryDecodeFromChars throws FormatException on an illegal character instead of returning false.
            || !Base64Url.IsValid(token)
            || !Base64Url.TryDecodeFromChars(token, buffer, out var written)
            || written != _Length
            || buffer[0] != _Version
        )
        {
            throw new ArgumentException(_InvalidTokenMessage, paramName);
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(buffer[1..]);

        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentException(_InvalidTokenMessage, paramName);
        }

        var id = BinaryPrimitives.ReadInt64BigEndian(buffer[(1 + sizeof(long))..]);

        return (new DateTime(ticks, DateTimeKind.Utc), id);
    }
}
