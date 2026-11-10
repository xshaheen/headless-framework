// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Headless.Blobs;

/// <summary>
/// The one entry the endpoint writes per request once it has checked the token: the operation, the outcome, the
/// expiry, and a fingerprint of the token. Every failure answers 404, so this entry is the only place that says why.
/// </summary>
internal static partial class BlobSignedUrlAccessLog
{
    public const string Category = "Headless.Blobs.SignedUrlEndpoint";

    /// <summary>
    /// The first 12 hex characters of the token's SHA-256. The token is Data Protection ciphertext with a random IV,
    /// so a short unkeyed hash cannot be reversed or guessed, and the same URL shows the same fingerprint on every
    /// replica, which lets an operator match the entry to the code that minted the URL.
    /// </summary>
    public static string Fingerprint(string token)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(token), hash);

        return Convert.ToHexStringLower(hash[..6]);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "SignedUrlAccess",
        Level = LogLevel.Information,
        Message = "Signed blob URL {Operation} {Outcome}; token {TokenFingerprint}, expires {ExpiresAt}"
    )]
    public static partial void LogSignedUrlAccess(
        this ILogger logger,
        BlobSignedUrlAccess operation,
        string outcome,
        string tokenFingerprint,
        DateTimeOffset? expiresAt
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SignedUrlAccessWithLocation",
        Level = LogLevel.Information,
        Message = "Signed blob URL {Operation} {Outcome}; token {TokenFingerprint}, expires {ExpiresAt}, blob {Container}/{Path}"
    )]
    public static partial void LogSignedUrlAccess(
        this ILogger logger,
        BlobSignedUrlAccess operation,
        string outcome,
        string tokenFingerprint,
        DateTimeOffset? expiresAt,
        string container,
        string path
    );
}
