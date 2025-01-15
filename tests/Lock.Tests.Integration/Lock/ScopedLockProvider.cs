using Framework.Abstractions;
using Microsoft.Extensions.Logging;

namespace Tests.Lock;

public sealed class ScopedLockProvider : ILockProvider, IHaveLogger
{
    private string _keyPrefix;
    private bool _isLocked;
    private readonly System.Threading.Lock _lock = new();

    public ScopedLockProvider(ILockProvider lockProvider, string? scope = null)
    {
        UnscopedLockProvider = lockProvider;
        _isLocked = scope != null;
        Scope = !string.IsNullOrWhiteSpace(scope) ? scope.Trim() : null;

        _keyPrefix = Scope != null ? $"{Scope}:" : string.Empty;
    }

    public ILockProvider UnscopedLockProvider { get; }

    public string? Scope { get; private set; }

    ILogger IHaveLogger.Logger => UnscopedLockProvider.GetLogger();

    public void SetScope(string scope)
    {
        if (_isLocked)
        {
            throw new InvalidOperationException("Scope can't be changed after it has been set");
        }

        lock (_lock)
        {
            if (_isLocked)
            {
                throw new InvalidOperationException("Scope can't be changed after it has been set");
            }

            _isLocked = true;
            Scope = !string.IsNullOrWhiteSpace(scope) ? scope.Trim() : null;
            _keyPrefix = Scope != null ? $"{Scope}:" : string.Empty;
        }
    }

    public Task<ILock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        bool releaseOnDispose = true,
        CancellationToken acquireAbortToken = default
    )
    {
        return UnscopedLockProvider.TryAcquireAsync(
            _GetScopedLockProviderKey(resource),
            timeUntilExpires,
            releaseOnDispose,
            acquireAbortToken
        );
    }

    public Task<bool> IsLockedAsync(string resource)
    {
        return UnscopedLockProvider.IsLockedAsync(_GetScopedLockProviderKey(resource));
    }

    public Task ReleaseAsync(string resource, string lockId)
    {
        return UnscopedLockProvider.ReleaseAsync(resource, lockId);
    }

    public Task RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        return UnscopedLockProvider.RenewAsync(resource, lockId, timeUntilExpires);
    }

    private string _GetScopedLockProviderKey(string key)
    {
        return string.Concat(_keyPrefix, key);
    }
}
