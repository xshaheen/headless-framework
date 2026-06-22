// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedSemaphoreStorage"/> for distributed counting
/// semaphores. Holder slots are stored in a Redis sorted set (ZSET) keyed by
/// <c>{resource}:holders</c>, where each member is a lease id and the score is the
/// expiry timestamp in milliseconds. A companion persistent key <c>fence:{resource}</c> is
/// incremented (INCR) on each successful acquire to issue a monotonically increasing fencing token.
/// </summary>
/// <remarks>
/// <para>
/// The resource name must not contain <c>{</c> or <c>}</c> characters — the key layout wraps the
/// resource in a Redis hash-tag (<c>{resource}</c>) so the holders ZSET and the fence counter always
/// land on the same cluster hash slot and can participate in atomic Lua scripts.
/// </para>
/// <para>
/// Expired slots are pruned lazily by <see cref="TryAcquireSemaphoreWithFenceScriptDefinition"/>
/// (ZREMRANGEBYSCORE before capacity check) and counted correctly read-only by
/// <see cref="GetSemaphoreCountScriptDefinition"/> (ZCOUNT with an exclusive lower bound).
/// </para>
/// <para>
/// Underlying StackExchange.Redis errors (e.g. <see cref="StackExchange.Redis.RedisException"/>) propagate
/// to the caller unless explicitly caught by this class.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RedisDistributedSemaphoreStorage(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedSemaphoreStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    /// <summary>
    /// Atomically acquires a semaphore slot for <paramref name="resource"/> identified by
    /// <paramref name="leaseId"/> when fewer than <paramref name="maxCount"/> non-expired slots are
    /// held, and returns a fencing token on success.
    /// Uses <see cref="TryAcquireSemaphoreWithFenceScriptDefinition"/> which prunes expired slots via
    /// ZREMRANGEBYSCORE before the capacity check, adds the new member with a score equal to the
    /// current Redis clock plus <paramref name="ttl"/> (in ms), and increments the persistent fence
    /// counter.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">A unique identifier for this slot; the caller is responsible for uniqueness.</param>
    /// <param name="maxCount">Maximum number of concurrent slot holders; must be ≥ 1.</param>
    /// <param name="ttl">Lease duration; must be greater than <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> with <c>Acquired = true</c> and a fencing token when a slot
    /// is granted; <see cref="DistributedLockAcquireResult.Failed"/> when the semaphore is at capacity.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCount"/> is less than 1 or <paramref name="ttl"/> is not greater than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    /// <exception cref="StackExchange.Redis.RedisServerException">Thrown when the Lua acquire script returns an unexpected result format.</exception>
    public async ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _TryAcquireSemaphoreAsync(
                keys.HoldersKey,
                keys.FenceKey,
                leaseId,
                maxCount,
                ttl,
                cancellationToken
            )
            .ConfigureAwait(false);

        return result.Acquired
            ? new DistributedLockAcquireResult(Acquired: true, result.FencingToken)
            : DistributedLockAcquireResult.Failed;
    }

    /// <summary>
    /// Atomically extends the expiry of the caller's semaphore slot when the slot is still present in
    /// the holders ZSET. Uses <see cref="TryExtendSemaphoreScriptDefinition"/> — a Lua script that
    /// checks for the member's existence via ZSCORE (so GT suppression on unchanged score is handled),
    /// then updates the score with ZADD XX GT to the new expiry, and refreshes the key's safety TTL.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id of the slot to extend.</param>
    /// <param name="ttl">New lease duration from now; must be greater than <see cref="TimeSpan.Zero"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the slot was found and its expiry was extended; <see langword="false"/> when the slot was not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ttl"/> is not greater than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> TryExtendAsync(
        string resource,
        string leaseId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, leaseId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Read-only check of whether the caller's semaphore slot is still live (its expiry score has
    /// not yet passed). Uses <see cref="ValidateSemaphoreScriptDefinition"/> — a Lua script that reads
    /// the slot's score via ZSCORE and compares it to the current Redis clock. Does NOT prune expired
    /// slots; use only for self-validation by an existing holder in a monitoring loop.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id to validate.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the slot exists and its expiry score is still in the future; <see langword="false"/> otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> ValidateAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                ValidateSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, leaseId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Atomically releases the caller's semaphore slot by removing it from the holders ZSET.
    /// Uses <see cref="ReleaseSemaphoreScriptDefinition"/> — a Lua script that issues ZREM. Idempotent:
    /// if the member is not present the script returns 0 without error.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id of the slot to release.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the slot was present and removed; <see langword="false"/> when the slot was not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> ReleaseAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                ReleaseSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, leaseId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Returns the number of live (non-expired) slots in the semaphore for <paramref name="resource"/>.
    /// Uses <see cref="GetSemaphoreCountScriptDefinition"/> — a read-only Lua script that issues ZCOUNT
    /// with an exclusive lower bound of the current Redis clock milliseconds, so expired-but-unpruned
    /// slots are excluded without a write.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns>Number of slots whose expiry score is still in the future. Returns 0 when no slots are held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                GetSemaphoreCountScriptDefinition.Instance,
                new SemaphoreCountParams(keys.HoldersKey),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (long)result;
    }

    private async Task<(bool Acquired, long? FencingToken)> _TryAcquireSemaphoreAsync(
        RedisKey holdersKey,
        RedisKey fenceKey,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken
    )
    {
        var parameters = new SemaphoreAcquireParams(
            holdersKey,
            fenceKey,
            leaseId,
            maxCount,
            (long)ttl.TotalMilliseconds
        );
        var result = await scriptsLoader
            .EvaluateAsync(Db, TryAcquireSemaphoreWithFenceScriptDefinition.Instance, parameters, cancellationToken)
            .ConfigureAwait(false);
        var values = (RedisResult[]?)result;

        if (values is null || values.Length == 0)
        {
            throw new RedisServerException("Unexpected acquire semaphore script result.");
        }

        if ((int)values[0] <= 0)
        {
            return (false, null);
        }

        if (values.Length < 2)
        {
            throw new RedisServerException("Acquire semaphore script reported success without a fencing token.");
        }

        return (true, (long)values[1]);
    }

    private static SemaphoreSlotParams _GetSemaphoreSlotParameters(RedisKey holdersKey, string leaseId, TimeSpan? ttl)
    {
        var expiresValue = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new SemaphoreSlotParams(holdersKey, leaseId, expiresValue);
    }

    private static (RedisKey HoldersKey, RedisKey FenceKey) _GetKeys(string resource)
    {
        Argument.IsNotNullOrEmpty(resource);
        Ensure.False(
            resource.Contains('{', StringComparison.Ordinal) || resource.Contains('}', StringComparison.Ordinal),
            "Semaphore resources cannot contain '{' or '}' because Redis hash-tags are storage-owned."
        );

        var hashTag = "{" + resource + "}";

        return (hashTag + ":holders", "fence:" + hashTag);
    }
}
