// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.Logging;

namespace Tests.Scenarios;

/// <summary>
/// A deliberately broken lock for the negative control: once a resource has had a few grants released, every further
/// grant on it carries the resource's first token again, as a backend that lost its fence counter would. The protected
/// resource accepted a later token long before, so the issuer rule of <see cref="Oracle.FencingVerdict"/> must fire.
/// </summary>
public sealed class SabotagedDistributedLock(IDistributedLock inner) : IDistributedLock
{
    private const int _HonestReleasesPerResource = 3;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    /// <summary>How many grants carried a reused token.</summary>
    public int Sabotaged { get; private set; }

    public TimeProvider TimeProvider => inner.TimeProvider;

    public ILogger Logger => inner.Logger;

    public TimeSpan DefaultTimeUntilExpires => inner.DefaultTimeUntilExpires;

    public TimeSpan DefaultAcquireTimeout => inner.DefaultAcquireTimeout;

    public async Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _Sabotage(await inner.AcquireAsync(resource, options, cancellationToken).ConfigureAwait(false));

    public async Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        await inner.TryAcquireAsync(resource, options, cancellationToken).ConfigureAwait(false) is { } lease
            ? _Sabotage(lease)
            : null;

    public Task<bool> RenewAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    ) => inner.RenewAsync(resource, leaseId, timeUntilExpires, cancellationToken);

    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default) =>
        inner.GetLeaseIdAsync(resource, cancellationToken);

    public Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
        inner.ReleaseAsync(resource, leaseId, cancellationToken);

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default) =>
        inner.IsLockedAsync(resource, cancellationToken);

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default) =>
        inner.GetExpirationAsync(resource, cancellationToken);

    public Task<DistributedLockInfo?> GetLockInfoAsync(
        string resource,
        CancellationToken cancellationToken = default
    ) => inner.GetLockInfoAsync(resource, cancellationToken);

    public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
        CancellationToken cancellationToken = default
    ) => inner.ListActiveLocksAsync(cancellationToken);

    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
        inner.GetActiveLocksCountAsync(cancellationToken);

    private IDistributedLease _Sabotage(IDistributedLease lease)
    {
        var real = lease.FencingToken ?? throw new InvalidOperationException("The inner lock issued no token.");

        lock (_lock)
        {
            if (!_resources.TryGetValue(lease.Resource, out var state))
            {
                _resources[lease.Resource] = new(real, 0);

                return new TrackedLease(lease, real, this);
            }

            if (state.Released < _HonestReleasesPerResource)
            {
                return new TrackedLease(lease, real, this);
            }

            Sabotaged++;

            return new TrackedLease(lease, state.First, this);
        }
    }

    private void _Released(string resource)
    {
        lock (_lock)
        {
            if (_resources.TryGetValue(resource, out var state))
            {
                _resources[resource] = state with { Released = state.Released + 1 };
            }
        }
    }

    private sealed record ResourceState(LockFencingToken First, int Released);

    private sealed class TrackedLease(IDistributedLease inner, LockFencingToken token, SabotagedDistributedLock owner)
        : IDistributedLease
    {
        public string LeaseId => inner.LeaseId;

        public LockFencingToken? FencingToken => token;

        public string Resource => inner.Resource;

        public int RenewalCount => inner.RenewalCount;

        public DateTimeOffset AcquiredAt => inner.AcquiredAt;

        public TimeSpan TimeWaitedForLock => inner.TimeWaitedForLock;

        public CancellationToken LostToken => inner.LostToken;

        public bool CanObserveLoss => inner.CanObserveLoss;

        public async Task ReleaseAsync()
        {
            await inner.ReleaseAsync().ConfigureAwait(false);
            owner._Released(inner.Resource);
        }

        public Task<bool> RenewAsync(
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => inner.RenewAsync(timeUntilExpires, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
