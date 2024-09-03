using System.Runtime.CompilerServices;
using AsyncKeyedLock;
using Framework.Kernel.Checks;

namespace Framework.DistributedLocks.Local;

[PublicAPI]
public sealed class LocalDistributedLockProvider : IDistributedLockProvider, IDisposable
{
    private readonly AsyncKeyedLocker<string> _localSyncObjects =
        new(
            options: o =>
            {
                o.PoolSize = 20;
                o.PoolInitialFill = 1;
            },
            comparer: StringComparer.Ordinal
        );

    private readonly IDistributedLockKeyNormalizer _distributedLockKeyNormalizer;

    public LocalDistributedLockProvider(IDistributedLockKeyNormalizer distributedLockKeyNormalizer)
    {
        _distributedLockKeyNormalizer = distributedLockKeyNormalizer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeout = null,
        CancellationToken abortToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        if (timeout.HasValue)
        {
            Argument.IsPositive(timeout.Value);
        }

        var key = _distributedLockKeyNormalizer.NormalizeKey(resource);

        if (timeout is null)
        {
            var releaser = await _localSyncObjects.LockAsync(key, abortToken);

            return new LocalDistributedLock(resource, releaser);
        }

        var timeoutReleaser = await _localSyncObjects.LockAsync(key, timeout.Value, abortToken);

        if (timeoutReleaser.EnteredSemaphore)
        {
            timeoutReleaser.Dispose();

            return null;
        }

        return new LocalDistributedLock(resource, timeoutReleaser);
    }

    public Task<bool> IsLockedAsync(string resource)
    {
        var result = _localSyncObjects.IsInUse(resource);

        return Task.FromResult(result);
    }

    public void Dispose()
    {
        _localSyncObjects.Dispose();
    }
}
