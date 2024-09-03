using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Medallion.Threading;
using MadelsonLockProvider = Medallion.Threading.IDistributedLockProvider;

namespace Framework.DistributedLocks.Madelson;

[PublicAPI]
public sealed class MedallionDistributedLockProvider(
    MadelsonLockProvider madelsonDistributedLockProvider,
    IDistributedLockKeyNormalizer distributedLockKeyNormalizer,
    ICancellationTokenProvider cancellationTokenProvider
) : IDistributedLockProvider
{
    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeout = null,
        CancellationToken abortToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        var key = distributedLockKeyNormalizer.NormalizeKey(resource);

        var token = _FallbackToTokenProvider(abortToken);

        if (timeout is null)
        {
            await madelsonDistributedLockProvider.TryAcquireLockAsync(key, cancellationToken: token);
        }

        var handle = await madelsonDistributedLockProvider.TryAcquireLockAsync(key, timeout ?? default, token);

        return handle is null ? null : new MedallionDistributedLock(resource, handle);
    }

    private CancellationToken _FallbackToTokenProvider(CancellationToken abortToken)
    {
        return abortToken == default || abortToken == CancellationToken.None
            ? cancellationTokenProvider.Token
            : abortToken;
    }
}
