// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        return new AttemptResult(purpose, cacheKey, count, quota.Limit, retryAfter);
    }

    public async ValueTask ResetAsync(AttemptResult attempt, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(attempt);

        await cache.RemoveAsync(attempt.CacheKey, cancellationToken).ConfigureAwait(false);
    }

    private string _Fingerprint(string purpose, string subject)
    {
        // The purpose is in the MAC as well as the key, so one subject's fingerprint differs across purposes and a
        // leaked key for one flow does not identify the same person in another.
        var payload = Encoding.UTF8.GetBytes($"{_KeyVersion}\n{purpose}\n{subject}");

        return Base64Url.EncodeToString(HMACSHA256.HashData(_subjectKey, payload));
    }
}
