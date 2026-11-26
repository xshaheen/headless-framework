// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

internal sealed class CacheAttemptLimiter(
    ICache cache,
    TimeProvider timeProvider,
    AttemptSubjectFingerprinter fingerprinter,
    IOptions<AttemptLimiterOptions> options
) : IAttemptLimiter
{
    private readonly string _keyPrefix = options.Value.KeyPrefix;

    public async ValueTask<AttemptResult> AcquireAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    )
    {
        var counter = _Locate(purpose, subject, quota);

        // The expiry only reclaims memory: the window number in the key is what closes the window, so the store
        // re-arming the TTL on every increment cannot stretch it. Expiring when the window closes keeps each
        // counter alive no longer than it can be read.
        long count;

        try
        {
            count = await cache
                .IncrementAsync(counter.Key, 1L, counter.RetryAfter, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any store failure leaves the attempt uncounted; it is rethrown as one typed, scrubbed exception.
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
#pragma warning restore CA1031
        {
            throw new AttemptLimiterUnavailableException(purpose, exception);
        }

        return new AttemptResult(purpose, count, quota.Limit, counter.RetryAfter, resetToken: counter.Key);
    }

    public async ValueTask<AttemptSnapshot> PeekAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    )
    {
        var counter = _Locate(purpose, subject, quota);

        // A plain read: no increment and no write, so looking a subject up never spends its budget, never creates a
        // counter for an unseen subject, and never moves the counter's expiry.
        CacheValue<long> stored;

        try
        {
            stored = await cache.GetAsync<long>(counter.Key, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any store failure leaves the count unknown; it is rethrown as one typed, scrubbed exception.
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
#pragma warning restore CA1031
        {
            throw new AttemptLimiterUnavailableException(purpose, exception);
        }

        return new AttemptSnapshot(
            purpose,
            counter.Fingerprint,
            stored.HasValue ? Math.Max(0, stored.Value) : 0,
            quota.Limit,
            counter.WindowStartsAt,
            counter.WindowEndsAt,
            counter.RetryAfter
        );
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

        await _RemoveAsync(attempt.Purpose, cacheKey, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> ResetAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    )
    {
        var counter = _Locate(purpose, subject, quota);

        return _RemoveAsync(purpose, counter.Key, cancellationToken);
    }

    private async ValueTask<bool> _RemoveAsync(string purpose, string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any store failure leaves the counter in place; it is rethrown as one typed, scrubbed exception.
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
#pragma warning restore CA1031
        {
            throw new AttemptLimiterUnavailableException(purpose, exception);
        }
    }

    /// <summary>
    /// Finds the counter of the current window for a purpose, subject, and quota. Acquire, peek, and reset all go
    /// through here, so they can never disagree on which window or key a budget lives in.
    /// </summary>
    private CounterLocation _Locate(string purpose, string subject, AttemptQuota quota)
    {
        Argument.IsNotNullOrEmpty(purpose);
        Argument.IsNotNullOrEmpty(subject);
        Argument.IsNotNull(quota);

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var windowSeconds = (long)quota.Window.TotalSeconds;
        var windowId = now / windowSeconds;
        var windowStart = windowId * windowSeconds;
        var windowEnd = windowStart + windowSeconds;
        var fingerprint = fingerprinter.Compute(purpose, subject);

        // The window length is in the key so a changed window starts fresh rather than reinterpreting a bucket
        // numbered for another length.
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{_keyPrefix}:{AttemptSubjectFingerprinter.KeyVersion}:{purpose}:{windowSeconds}:{windowId}:{fingerprint}"
        );

        return new CounterLocation(
            cacheKey,
            fingerprint,
            DateTimeOffset.FromUnixTimeSeconds(windowStart),
            DateTimeOffset.FromUnixTimeSeconds(windowEnd),
            TimeSpan.FromSeconds(Math.Max(1, windowEnd - now))
        );
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is the store failing rather than the caller cancelling: the caller's own
    /// cancellation must surface as cancellation, so the request is reported as aborted, not as a 503.
    /// </summary>
    private static bool _IsStoreFailure(Exception exception, CancellationToken cancellationToken)
    {
        return !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
    }

    private readonly record struct CounterLocation(
        string Key,
        string Fingerprint,
        DateTimeOffset WindowStartsAt,
        DateTimeOffset WindowEndsAt,
        TimeSpan RetryAfter
    );
}
