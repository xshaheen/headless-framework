// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Signs a key-mode forwarded call and checks a received one. The key never travels: the calling node sends an
/// HMAC-SHA256 over the call, so a peer that receives a call learns nothing it can replay elsewhere or later.
/// </summary>
/// <remarks>
/// The signed string is the version tag, the timestamp, the nonce, the method, the request target as sent (path and
/// query, escaped), the hex SHA-256 of the body, and the hop, actor, and access headers, joined with <c>\n</c>. None of
/// those values can hold a raw line break, so two different calls never give the same string.
/// </remarks>
internal static class ForwardingSignature
{
    public const string Version = "v1";

    private const int _NonceBytes = 16;
    private const int _SignatureBytes = 32;
    private const string _TimestampField = "ts=";
    private const string _NonceField = "nonce=";
    private const string _SignatureField = "sig=";

    // A purpose label keeps the HMAC key apart from any other use of the same configured secret.
    private static readonly byte[] _Purpose = "headless-dashboard-forwarding-signature-v1"u8.ToArray();

    /// <summary>The lower-case hex SHA-256 of an empty body, sent for a call without content.</summary>
    public static readonly string EmptyBodyHash = Convert.ToHexStringLower(SHA256.HashData([]));

    /// <summary>Adds the signature header to <paramref name="request"/>, signed with <paramref name="key"/> at now.</summary>
    public static async Task SignAsync(
        HttpRequestMessage request,
        string key,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? EmptyBodyHash
            : Convert.ToHexStringLower(
                SHA256.HashData(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false))
            );

        var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_NonceBytes));
        var canonical = Canonical(
            timestamp,
            nonce,
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            body,
            _Header(request, ForwardingProtocol.HopHeader),
            _Header(request, ForwardingProtocol.ActorHeader),
            _Header(request, ForwardingProtocol.AccessHeader)
        );

        var signature = Base64Url.EncodeToString(Compute(key, canonical));

        request.Headers.Remove(ForwardingProtocol.SignatureHeader);
        request.Headers.TryAddWithoutValidation(
            ForwardingProtocol.SignatureHeader,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Version};{_TimestampField}{timestamp};{_NonceField}{nonce};{_SignatureField}{signature}"
            )
        );
    }

    /// <summary>The string both ends sign; see the remarks on <see cref="ForwardingSignature"/>.</summary>
    public static string Canonical(
        long timestamp,
        string nonce,
        string method,
        string target,
        string bodyHash,
        string hop,
        string actor,
        string access
    )
    {
        return string.Join(
            '\n',
            Version,
            timestamp.ToString(CultureInfo.InvariantCulture),
            nonce,
            method.ToUpperInvariant(),
            target,
            bodyHash,
            hop,
            actor,
            access
        );
    }

    /// <summary>The HMAC-SHA256 of <paramref name="canonical"/> under the key derived from <paramref name="key"/>.</summary>
    public static byte[] Compute(string key, string canonical)
    {
        var derived = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(key),
            outputLength: _SignatureBytes,
            salt: [],
            info: _Purpose
        );

        return HMACSHA256.HashData(derived, Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>Whether <paramref name="presented"/> is the signature of <paramref name="canonical"/> under the key.</summary>
    public static bool Matches(ReadOnlySpan<byte> presented, string key, string canonical)
    {
        return CryptographicOperations.FixedTimeEquals(presented, Compute(key, canonical));
    }

    /// <summary>
    /// Reads a signature header. Only the exact <c>v1;ts=..;nonce=..;sig=..</c> shape parses, with a 16-byte nonce and a
    /// 32-byte signature.
    /// </summary>
    public static bool TryParse(string header, out long timestamp, out string nonce, out byte[] signature)
    {
        timestamp = 0;
        nonce = string.Empty;
        signature = [];

        var parts = header.Split(';');

        if (
            parts.Length != 4
            || !string.Equals(parts[0], Version, StringComparison.Ordinal)
            || !parts[1].StartsWith(_TimestampField, StringComparison.Ordinal)
            || !parts[2].StartsWith(_NonceField, StringComparison.Ordinal)
            || !parts[3].StartsWith(_SignatureField, StringComparison.Ordinal)
            || !long.TryParse(
                parts[1].AsSpan(_TimestampField.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out timestamp
            )
        )
        {
            return false;
        }

        var nonceText = parts[2][_NonceField.Length..];
        var signatureText = parts[3][_SignatureField.Length..];

        if (!_TryDecode(nonceText, _NonceBytes, out _) || !_TryDecode(signatureText, _SignatureBytes, out var decoded))
        {
            return false;
        }

        nonce = nonceText;
        signature = decoded;

        return true;
    }

    private static bool _TryDecode(string text, int length, out byte[] bytes)
    {
        bytes = [];

        if (!Base64Url.IsValid(text, out var decodedLength) || decodedLength != length)
        {
            return false;
        }

        bytes = Base64Url.DecodeFromChars(text);

        return true;
    }

    private static string _Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : string.Empty;
}
