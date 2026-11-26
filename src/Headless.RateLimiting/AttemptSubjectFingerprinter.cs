// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

/// <summary>
/// Turns a purpose and subject into the pseudonymous fingerprint the attempt limiter keys its counters on: a base64url
/// HMAC-SHA256 under <see cref="AttemptLimiterOptions.SubjectKey"/>.
/// </summary>
/// <remarks>
/// Resolve it from the container to show or log which budget an operation touched without exposing the subject. It
/// is the only code that derives fingerprints, so a value it returns always matches the last segment of the counter
/// key the built-in limiter writes for the same purpose and subject. The fingerprint is stable until
/// <see cref="AttemptLimiterOptions.SubjectKey"/> rotates, and differs across purposes for the same subject.
/// </remarks>
[PublicAPI]
public sealed class AttemptSubjectFingerprinter
{
    // Bumped when the fingerprint payload changes, so old counters are abandoned instead of misread. The limiter
    // carries the same version in its key layout.
    internal const string KeyVersion = "v1";

    private const int _StackallocThreshold = 256;

    private readonly byte[] _subjectKey;

    /// <summary>Initializes a fingerprinter over the configured <see cref="AttemptLimiterOptions.SubjectKey"/>.</summary>
    /// <param name="options">The limiter options; their <see cref="AttemptLimiterOptions.SubjectKey"/> is read once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The subject key is not valid base64.</exception>
    public AttemptSubjectFingerprinter(IOptions<AttemptLimiterOptions> options)
    {
        Argument.IsNotNull(options);
        _subjectKey = Convert.FromBase64String(options.Value.SubjectKey);
    }

    /// <summary>Computes the fingerprint of <paramref name="subject"/> under <paramref name="purpose"/>.</summary>
    /// <param name="purpose">The limiter purpose, exactly as passed to <see cref="IAttemptLimiter"/>.</param>
    /// <param name="subject">The normalized subject, exactly as passed to <see cref="IAttemptLimiter"/>.</param>
    /// <returns>A 43-character base64url string that reveals neither the subject nor the key.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> or <paramref name="subject"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public string Compute(string purpose, string subject)
    {
        Argument.IsNotNullOrEmpty(purpose);
        Argument.IsNotNullOrEmpty(subject);

        // The purpose is in the MAC as well as the key, so one subject's fingerprint differs across purposes and a
        // leaked key for one flow does not identify the same person in another.
        // A typical email or phone subject fits on the stack, which spares an allocation per rate-limited request.
        // Nothing bounds the caller's strings, though, and the subject is often a raw request field an attacker
        // sizes, so a larger payload is rented: a stackalloc sized by input overflows the stack and kills the process.
        var keyVersionLength = Encoding.UTF8.GetByteCount(KeyVersion);
        var purposeLength = Encoding.UTF8.GetByteCount(purpose);
        var subjectLength = Encoding.UTF8.GetByteCount(subject);
        var payloadLength = keyVersionLength + purposeLength + subjectLength + 2;

        var rented = payloadLength > _StackallocThreshold ? ArrayPool<byte>.Shared.Rent(payloadLength) : null;
        var payload = rented is null ? stackalloc byte[payloadLength] : rented.AsSpan(0, payloadLength);

        try
        {
            var written = Encoding.UTF8.GetBytes(KeyVersion, payload);
            payload[written++] = (byte)'\n';
            written += Encoding.UTF8.GetBytes(purpose, payload[written..]);
            payload[written++] = (byte)'\n';
            _ = Encoding.UTF8.GetBytes(subject, payload[written..]);

            return Base64Url.EncodeToString(HMACSHA256.HashData(_subjectKey, payload));
        }
        finally
        {
            if (rented is not null)
            {
                // The subject is personal data the fingerprint exists to hide; a pooled array outlives this call.
                CryptographicOperations.ZeroMemory(payload);
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
