// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

internal enum BlobSignedUrlAccess : byte
{
    Download = 1,
    Upload = 2,
}

/// <summary>What a verified token grants: one verb on one blob of one store, until an instant.</summary>
/// <param name="Store">The named store, or <see langword="null"/> for the default (unkeyed) store.</param>
internal sealed record BlobSignedUrlGrant(
    BlobSignedUrlAccess Access,
    string? Store,
    BlobLocation Location,
    DateTimeOffset ExpiresAt,
    string? ContentType,
    long? MaxLength
);

/// <summary>Mints and verifies the data-protection tokens carried in signed blob URLs.</summary>
/// <remarks>
/// The token authenticates every field, so a URL for one blob, store, or verb cannot be replayed for another. Expiry
/// travels inside the payload and is checked against the injected <see cref="TimeProvider"/> rather than through
/// <c>ITimeLimitedDataProtector</c>, which reads the system clock directly.
/// </remarks>
internal sealed class BlobSignedUrlSigner(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    IOptions<BlobSignedUrlOptions> options
)
{
    // Bumping the version invalidates every URL minted with the previous payload layout instead of misreading it.
    private const byte _PayloadVersion = 1;

    // Resolved on first use, not at construction. The signer is built while its store is being built, and a key ring
    // persisted to blob storage (PersistKeysToBlobStorage) resolves that same store when data protection starts, so an
    // eager dependency would deadlock the container on the half-built singleton.
    private readonly Lazy<IDataProtector> _protector = new(() =>
        serviceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Headless.Blobs.SignedUrl")
    );

    public Uri CreateUrl(
        BlobSignedUrlAccess access,
        string? store,
        BlobLocation location,
        TimeSpan expiry,
        PresignedUploadConstraints? constraints
    )
    {
        var grant = new BlobSignedUrlGrant(
            access,
            store,
            location,
            timeProvider.GetUtcNow().Add(expiry),
            constraints?.ContentType,
            constraints?.MaxLength
        );

        var token = Base64Url.EncodeToString(_protector.Value.Protect(_Serialize(grant)));
        var settings = options.Value;

        return new Uri(
            $"{settings.BaseUrl!.AbsoluteUri.TrimEnd('/')}{settings.RoutePrefix.TrimEnd('/')}/{token}",
            UriKind.Absolute
        );
    }

    /// <summary>
    /// Verifies <paramref name="token"/> and returns its grant when it is authentic, unexpired, and for
    /// <paramref name="access"/>. Every failure collapses into <see langword="false"/> so the endpoint answers all of
    /// them with the same 404.
    /// </summary>
    public bool TryRead(string token, BlobSignedUrlAccess access, [NotNullWhen(true)] out BlobSignedUrlGrant? grant)
    {
        grant = null;

        BlobSignedUrlGrant candidate;

        try
        {
            candidate = _Deserialize(_protector.Value.Unprotect(Base64Url.DecodeFromChars(token)));
        }
        catch (Exception e)
            when (e is FormatException or CryptographicException or EndOfStreamException or ArgumentException)
        {
            return false;
        }

        if (candidate.Access != access || candidate.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return false;
        }

        grant = candidate;

        return true;
    }

    private static byte[] _Serialize(BlobSignedUrlGrant grant)
    {
#pragma warning disable MA0045 // False positive: a MemoryStream and its BinaryWriter have no async disposal work, and minting is synchronous.
        using var stream = new MemoryStream();

        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(_PayloadVersion);
            writer.Write((byte)grant.Access);
            _WriteNullable(writer, grant.Store);
            writer.Write(grant.Location.Container);
            writer.Write(grant.Location.Path);
            writer.Write(grant.ExpiresAt.ToUnixTimeMilliseconds());
            _WriteNullable(writer, grant.ContentType);
            writer.Write(grant.MaxLength.HasValue);

            if (grant.MaxLength.HasValue)
            {
                writer.Write(grant.MaxLength.Value);
            }
        }
#pragma warning restore MA0045

        return stream.ToArray();
    }

    private static BlobSignedUrlGrant _Deserialize(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);

        if (reader.ReadByte() != _PayloadVersion)
        {
            throw new FormatException("Unsupported signed blob URL payload version.");
        }

        var access = (BlobSignedUrlAccess)reader.ReadByte();
        var store = _ReadNullable(reader);
        var location = new BlobLocation(container: reader.ReadString(), path: reader.ReadString());
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
        var contentType = _ReadNullable(reader);
        long? maxLength = reader.ReadBoolean() ? reader.ReadInt64() : null;

        return new BlobSignedUrlGrant(access, store, location, expiresAt, contentType, maxLength);
    }

    private static void _WriteNullable(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);

        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? _ReadNullable(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadString() : null;
    }
}
