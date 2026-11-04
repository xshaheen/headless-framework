// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.Caching;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

internal sealed class CacheAttemptLimiter(
    ICache cache,
    TimeProvider timeProvider,
    IOptions<AttemptLimiterOptions> options
) : IAttemptLimiter
{
    // Bumped when the key layout or fingerprint payload changes, so old counters are abandoned instead of misread.
    private const string _KeyVersion = "v1";

    private const int _StackallocThreshold = 256;

    private readonly byte[] _subjectKey = Convert.FromBase64String(options.Value.SubjectKey);
    private readonly string _keyPrefix = options.Value.KeyPrefix;

    public async ValueTask<AttemptResult> AcquireAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(purpose);
        Argument.IsNotNullOrEmpty(subject);
        Argument.IsNotNull(quota);

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var windowSeconds = (long)quota.Window.TotalSeconds;
        var windowId = now / windowSeconds;
        var retryAfter = TimeSpan.FromSeconds(Math.Max(1, ((windowId + 1) * windowSeconds) - now));

        // The window length is in the key so a changed window starts fresh rather than reinterpreting a bucket
        // numbered for another length.
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{_keyPrefix}:{_KeyVersion}:{purpose}:{windowSeconds}:{windowId}:{_Fingerprint(purpose, subject)}"
        );

        // The expiry only reclaims memory: the window number in the key is what closes the window, so the store
        // re-arming the TTL on every increment cannot stretch it. Expiring when the window closes keeps each
        // counter alive no longer than it can be read.
        var count = await cache.IncrementAsync(cacheKey, 1L, retryAfter, cancellationToken).ConfigureAwait(false);

        return new AttemptResult(purpose, count, quota.Limit, retryAfter, resetToken: cacheKey);
    }

    public async ValueTask ResetAsync(AttemptResult attempt, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(attempt);

        // A result without a token was built by hand (a test stub) or by another limiter; removing nothing would
        // report a reset that never happened.
        if (attempt.ResetToken is not { } cacheKey)
        {
            throw new ArgumentException("The attempt carries no reset token from this limiter.", nameof(attempt));
        }

        await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
    }

    private string _Fingerprint(string purpose, string subject)
    {
        // The purpose is in the MAC as well as the key, so one subject's fingerprint differs across purposes and a
        // leaked key for one flow does not identify the same person in another.
        // A typical email or phone subject fits on the stack, which spares an allocation per rate-limited request.
        // Nothing bounds the caller's strings, though, and the subject is often a raw request field an attacker
        // sizes, so a larger payload is rented: a stackalloc sized by input overflows the stack and kills the process.
        var keyVersionLength = Encoding.UTF8.GetByteCount(_KeyVersion);
        var purposeLength = Encoding.UTF8.GetByteCount(purpose);
        var subjectLength = Encoding.UTF8.GetByteCount(subject);
        var payloadLength = keyVersionLength + purposeLength + subjectLength + 2;

        var rented = payloadLength > _StackallocThreshold ? ArrayPool<byte>.Shared.Rent(payloadLength) : null;
        var payload = rented is null ? stackalloc byte[payloadLength] : rented.AsSpan(0, payloadLength);

        try
        {
            var written = Encoding.UTF8.GetBytes(_KeyVersion, payload);
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
