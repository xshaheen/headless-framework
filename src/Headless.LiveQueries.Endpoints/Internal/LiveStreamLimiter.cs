// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Bounds what streams cost the host: how many are open, per user and in total, and how often and how many at a
/// time they run again. It only admits, counts and makes a stream's own loop wait; it never runs anything.
/// </summary>
#pragma warning disable CA1001 // The semaphore holds no handle unless its wait handle is read, and a stream that outlives the container must still be able to return its turn.
internal sealed class LiveStreamLimiter
#pragma warning restore CA1001
{
    private readonly int _maxStreams;
    private readonly int _maxStreamsPerUser;
    private readonly TimeSpan _minRerunInterval;
    private readonly SemaphoreSlim _reruns;
    private readonly Turn _turn;
    private readonly TimeProvider _clock;
    private readonly LiveEndpointsMetrics _metrics;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _streamsByOwner = new(StringComparer.Ordinal);
    private int _streams;

    public LiveStreamLimiter(
        IOptions<LiveEndpointsOptions> options,
        [FromKeyedServices(LiveEndpointsOptions.RerunClockServiceKey)] TimeProvider clock,
        LiveEndpointsMetrics metrics
    )
    {
        var value = options.Value;
        _maxStreams = value.MaxStreams;
        _maxStreamsPerUser = value.MaxStreamsPerUser;
        _minRerunInterval = value.MinRerunInterval;
        _reruns = new SemaphoreSlim(value.MaxConcurrentReruns, value.MaxConcurrentReruns);
        _turn = new Turn(_reruns);
        _clock = clock;
        _metrics = metrics;
    }

    /// <summary>Gets the reading of the re-run clock now, to pass back to <see cref="EnterRerunAsync" />.</summary>
    public long Now => _clock.GetTimestamp();

    /// <summary>Returns who a stream of <paramref name="httpContext" /> counts against.</summary>
    public static string OwnerOf(HttpContext httpContext)
    {
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var id =
                user.FindFirst(UserClaimTypes.UserId)?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.Identity.Name;

            if (!string.IsNullOrEmpty(id))
            {
                // The same user id in two tenants is two users.
                return $"user:{user.FindFirst(UserClaimTypes.TenantId)?.Value}:{id}";
            }
        }

        // Anonymous callers cannot be told apart, so the address stands in for the user.
        return $"address:{httpContext.Connection.RemoteIpAddress?.ToString()}";
    }

    /// <summary>Counts one more stream for <paramref name="owner" />, unless a limit is reached.</summary>
    /// <param name="owner">Who the stream counts against; see <see cref="OwnerOf" />.</param>
    /// <returns>The admitted stream, to dispose when it ends, or <see langword="null" /> when a limit is reached.</returns>
    public IDisposable? TryOpen(string owner)
    {
        lock (_lock)
        {
            var held = _streamsByOwner.GetValueOrDefault(owner);

            if (_streams < _maxStreams && held < _maxStreamsPerUser)
            {
                _streams++;
                _streamsByOwner[owner] = held + 1;

                return new OpenStream(this, owner);
            }
        }

        _metrics.StreamRejected();

        return null;
    }

    /// <summary>
    /// Waits until a stream whose last run started at <paramref name="lastRunStarted" /> may run again: first for
    /// the rest of the minimum interval, then for one of the host's re-run turns.
    /// </summary>
    /// <param name="lastRunStarted">The <see cref="Now" /> reading taken when the stream's last run started.</param>
    /// <param name="cancellationToken">Ends the wait when the stream ends.</param>
    /// <returns>The turn. Dispose it when the run has ended.</returns>
    public async ValueTask<IDisposable?> EnterRerunAsync(long lastRunStarted, CancellationToken cancellationToken)
    {
        var remaining = _minRerunInterval - _clock.GetElapsedTime(lastRunStarted);

        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _clock, cancellationToken).ConfigureAwait(false);
        }

        if (!await _reruns.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _metrics.RerunQueued();
            await _reruns.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return _turn;
    }

    private void _Close(string owner)
    {
        lock (_lock)
        {
            _streams--;
            var held = _streamsByOwner[owner] - 1;

            if (held == 0)
            {
                _streamsByOwner.Remove(owner);
            }
            else
            {
                _streamsByOwner[owner] = held;
            }
        }
    }

    private sealed class OpenStream(LiveStreamLimiter limiter, string owner) : IDisposable
    {
        private int _closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                limiter._Close(owner);
            }
        }
    }

    // One instance for every run: the hub disposes a turn exactly once, when the run it was taken for has ended.
    private sealed class Turn(SemaphoreSlim reruns) : IDisposable
    {
        public void Dispose() => reruns.Release();
    }
}
