// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Settings;

/// <summary>Why a snapshot is being reloaded; decides whether a reload that saw no change re-reads again.</summary>
internal enum SettingsSnapshotReloadReason
{
    /// <summary>The first load, on demand or in the background. Undefined names and a bind failure throw to the caller.</summary>
    Initial = 1,

    /// <summary>A <see cref="SettingChangedMessage"/> named settings this snapshot tracks.</summary>
    Message = 2,

    /// <summary>The every-instance subscription was established, first or after a gap.</summary>
    Establishment = 3,

    /// <summary>The backstop timer fired.</summary>
    Backstop = 4,

    /// <summary>A delayed re-read scheduled by an earlier message or establishment reload. Never schedules more.</summary>
    Settle = 5,
}

/// <summary>The type-erased side of a registered snapshot that the consumer and the hosted service drive.</summary>
internal interface ISettingsSnapshotEntry
{
    /// <summary>The setting names the snapshot is bound from.</summary>
    IReadOnlySet<string> Names { get; }

    /// <summary>The backstop re-read interval.</summary>
    TimeSpan Backstop { get; }

    /// <summary>Whether the first load has completed.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Validates the names and performs the first load unless it already completed. Throws when either fails, so the
    /// caller decides whether to retry.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads a loaded snapshot. Does nothing until the first load completes: that load owns name validation, and a value
    /// nobody read yet has no missed change to recover. Failures are logged, never thrown, except cancellation.
    /// </summary>
    /// <param name="reason">Why the reload runs.</param>
    /// <param name="announcedNames">The tracked names a message announced, for <see cref="SettingsSnapshotReloadReason.Message"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task ReloadAsync(
        SettingsSnapshotReloadReason reason,
        IReadOnlyCollection<string>? announcedNames,
        CancellationToken cancellationToken
    );
}

/// <summary>Builds a <typeparamref name="T"/> from Global settings and keeps it current. See <see cref="ISettingsSnapshot{T}"/>.</summary>
/// <remarks>
/// <para>
/// Reloads are serialized, so the consumer, the subscription hook, the backstop, and settle re-reads never interleave and
/// <see cref="Revision"/> never moves backward. The value and <see cref="Revision"/> live in one immutable
/// state swapped atomically, so a reader never sees a new value with an old revision.
/// </para>
/// <para>
/// With a hybrid setting cache, a peer can receive <see cref="SettingChangedMessage"/> before its own
/// <c>CacheInvalidationMessage</c>; the two travel on separate subscriptions with no ordering between them. A reload in
/// that window reads the old L1 value. So a message reload whose announced names did not change, and an establishment
/// reload that changed nothing, re-read a few more times over the next seconds. An establishment reload settles too,
/// because the hybrid cache flushes its L1 in its own establishment hook, with no ordering against this one. The setting
/// cache offers no local-only read or eviction to avoid the window instead, and removing the entry would evict the shared
/// tier and broadcast.
/// </para>
/// <para>
/// Only <see cref="SettingsSnapshotReloadReason.Initial"/> loads an unloaded snapshot. A message, establishment, or settle
/// reload before then does nothing, so the subscription hook never waits on the store during host startup and every first
/// load validates the names.
/// </para>
/// </remarks>
internal sealed partial class SettingsSnapshot<T> : ISettingsSnapshot<T>, ISettingsSnapshotEntry, IDisposable
{
    /// <summary>Delays between settle re-reads, cumulative from the reload that scheduled them.</summary>
    internal static readonly TimeSpan[] SettleDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromSeconds(3),
    ];

    private readonly HashSet<string> _names;
    private readonly Func<IReadOnlyDictionary<string, string?>, T> _bind;
    private readonly ISettingManager _settingManager;
    private readonly ISettingDefinitionManager _definitionManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
#pragma warning disable CA2213 // False positive: never touching AvailableWaitHandle leaves nothing to release, and disposing it would strand the reloads waiting on it at shutdown.
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
#pragma warning restore CA2213
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _listenersLock = new();
    private Action<T, long>[] _listeners = [];
    private volatile State? _state;
    private bool _namesValidated;
    private int _disposed;

    public SettingsSnapshot(
        IReadOnlyCollection<string> names,
        Func<IReadOnlyDictionary<string, string?>, T> bind,
        TimeSpan backstop,
        ISettingManager settingManager,
        ISettingDefinitionManager definitionManager,
        TimeProvider timeProvider,
        ILogger<SettingsSnapshot<T>> logger
    )
    {
        _names = new HashSet<string>(names, StringComparer.Ordinal);
        Names = _names.ToFrozenSet(StringComparer.Ordinal);
        _bind = bind;
        Backstop = backstop;
        _settingManager = settingManager;
        _definitionManager = definitionManager;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlySet<string> Names { get; }

    public TimeSpan Backstop { get; }

    public bool TryGetCurrent([MaybeNullWhen(false)] out T value)
    {
        if (_state is { } state)
        {
            value = state.Value;
            return true;
        }

        value = default;
        return false;
    }

    public long Revision => _state?.Revision ?? 0;

    public bool IsLoaded => _state is not null;

    public ValueTask<T> GetAsync(CancellationToken cancellationToken = default)
    {
        return _state is { } state ? ValueTask.FromResult(state.Value) : _LoadAndGetAsync(cancellationToken);
    }

    public IDisposable OnChange(Action<T, long> listener)
    {
        Argument.IsNotNull(listener);

        lock (_listenersLock)
        {
            _listeners = [.. _listeners, listener];
        }

        return new Subscription(this, listener);
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return;
        }

        await _ReloadAsync(SettingsSnapshotReloadReason.Initial, announcedNames: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<T> _LoadAndGetAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        // An Initial reload returns only once a state exists, and a state is never cleared.
        return _state!.Value;
    }

    private async Task _ValidateNamesAsync(CancellationToken cancellationToken)
    {
        var undefined = new List<string>();

        foreach (var name in _names)
        {
            if (await _definitionManager.FindAsync(name, cancellationToken).ConfigureAwait(false) is null)
            {
                undefined.Add(name);
            }
        }

        if (undefined.Count != 0)
        {
            throw new InvalidOperationException(
                $"The settings snapshot of {typeof(T).Name} tracks undefined settings: {string.Join(", ", undefined)}."
            );
        }

        _namesValidated = true;
    }

    public Task ReloadAsync(
        SettingsSnapshotReloadReason reason,
        IReadOnlyCollection<string>? announcedNames,
        CancellationToken cancellationToken
    )
    {
        return _TryReloadAsync(reason, announcedNames, cancellationToken);
    }

    /// <summary>
    /// Reloads and returns the values read, or <see langword="null"/> when nothing was read: the snapshot is not loaded yet,
    /// or the reload failed and was logged.
    /// </summary>
    private async Task<FrozenDictionary<string, string?>?> _TryReloadAsync(
        SettingsSnapshotReloadReason reason,
        IReadOnlyCollection<string>? announcedNames,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _ReloadAsync(reason, announcedNames, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
            // The host is shutting down; nothing reads this snapshot any more.
            return null;
        }
        catch (Exception e)
        {
            LogReloadFailed(_logger, e, typeof(T).Name, reason);

            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Pending settle re-reads observe the cancellation and stop. The reload lock stays undisposed: a reload holding it
        // still releases it, and one waiting on it acquires it and then finds the snapshot disposed instead of hanging.
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private async Task<FrozenDictionary<string, string?>?> _ReloadAsync(
        SettingsSnapshotReloadReason reason,
        IReadOnlyCollection<string>? announcedNames,
        CancellationToken cancellationToken
    )
    {
        FrozenDictionary<string, string?>? before;
        FrozenDictionary<string, string?> after;

        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

            if (_state is { } loaded)
            {
                // Concurrent first loads queue on the lock; the ones after the winner find the state and read nothing.
                if (reason is SettingsSnapshotReloadReason.Initial)
                {
                    return loaded.Raw;
                }
            }
            else if (reason is not SettingsSnapshotReloadReason.Initial)
            {
                // See the class remarks: only the first load loads.
                return null;
            }
            else if (!_namesValidated)
            {
                // Under the lock, so concurrent first loads look the definitions up once.
                await _ValidateNamesAsync(cancellationToken).ConfigureAwait(false);
            }

            before = _state?.Raw;
            after = await _ReadAsync(cancellationToken).ConfigureAwait(false);

            if (before is null || !_SameValues(before, after, _names))
            {
                _Apply(reason, before, after);
            }
        }
        finally
        {
            _reloadLock.Release();
        }

        // Settle only when the values this trigger is about did not show up yet; see the class remarks.
        var watched = reason switch
        {
            SettingsSnapshotReloadReason.Message => announcedNames,
            SettingsSnapshotReloadReason.Establishment => _names,
            _ => null,
        };

        // Compare what was read, not the state after binding: a value bind rejected was still seen, and re-reading it
        // would only fail and log again. With no earlier state there is nothing the trigger could be waiting to replace.
        if (watched is { Count: > 0 } && before is not null && _SameValues(before, after, watched))
        {
            _ = _SettleAsync(before, watched);
        }

        return after;
    }

    private void _Apply(
        SettingsSnapshotReloadReason reason,
        FrozenDictionary<string, string?>? before,
        FrozenDictionary<string, string?> after
    )
    {
        T value;

        try
        {
            value = _bind(after);
        }
        catch (Exception e) when (reason is not SettingsSnapshotReloadReason.Initial)
        {
            // Keep serving the last good value: one bad operator value must not take a running process down.
            LogBindFailed(_logger, e, typeof(T).Name, string.Join(", ", _Changed(before, after)));

            return;
        }

        var state = new State(value, (_state?.Revision ?? 0) + 1, after);
        _state = state;

        Action<T, long>[] listeners;

        lock (_listenersLock)
        {
            listeners = _listeners;
        }

        foreach (var listener in listeners)
        {
            try
            {
                listener(state.Value, state.Revision);
            }
            catch (Exception e)
            {
                LogListenerFailed(_logger, e, typeof(T).Name, state.Revision);
            }
        }
    }

    private async Task _SettleAsync(FrozenDictionary<string, string?> baseline, IReadOnlyCollection<string> watched)
    {
        CancellationToken token;

        try
        {
            token = _disposeCts.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            foreach (var delay in SettleDelays)
            {
                await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
                var read = await _TryReloadAsync(SettingsSnapshotReloadReason.Settle, announcedNames: null, token)
                    .ConfigureAwait(false);

                if (read is not null && !_SameValues(baseline, read, watched))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The snapshot was disposed with the host.
        }
        catch (ObjectDisposedException)
        {
            // The snapshot was disposed between two re-reads.
        }
    }

    private async Task<FrozenDictionary<string, string?>> _ReadAsync(CancellationToken cancellationToken)
    {
        var values = await _settingManager
            .GetAllAsync(_names, SettingValueProviderNames.Global, providerKey: null, fallback: true, cancellationToken)
            .ConfigureAwait(false);

        // The manager omits names with no value; every tracked name is present here so bind sees a complete map.
        var raw = new Dictionary<string, string?>(_names.Count, StringComparer.Ordinal);

        foreach (var name in _names)
        {
            raw[name] = null;
        }

        foreach (var value in values)
        {
            if (_names.Contains(value.Name))
            {
                raw[value.Name] = value.Value;
            }
        }

        return raw.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static bool _SameValues(
        FrozenDictionary<string, string?> left,
        FrozenDictionary<string, string?> right,
        IEnumerable<string> names
    )
    {
        foreach (var name in names)
        {
            if (!string.Equals(left.GetValueOrDefault(name), right.GetValueOrDefault(name), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<string> _Changed(
        FrozenDictionary<string, string?>? before,
        FrozenDictionary<string, string?> after
    )
    {
        return before is null
            ? _names
            : _names.Where(name => !string.Equals(before[name], after[name], StringComparison.Ordinal));
    }

    private void _Unsubscribe(Action<T, long> listener)
    {
        lock (_listenersLock)
        {
            var index = Array.IndexOf(_listeners, listener);

            if (index >= 0)
            {
                _listeners = [.. _listeners[..index], .. _listeners[(index + 1)..]];
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "SettingsSnapshotBindFailed",
        Level = LogLevel.Error,
        Message = "Binding the settings snapshot of {SnapshotType} failed; keeping the last good value. Changed settings: {SettingNames}"
    )]
    private static partial void LogBindFailed(
        ILogger logger,
        Exception exception,
        string snapshotType,
        string settingNames
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SettingsSnapshotListenerFailed",
        Level = LogLevel.Error,
        Message = "A change listener of the settings snapshot of {SnapshotType} failed at revision {Revision}"
    )]
    private static partial void LogListenerFailed(
        ILogger logger,
        Exception exception,
        string snapshotType,
        long revision
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "SettingsSnapshotReloadFailed",
        Level = LogLevel.Error,
        Message = "Reloading the settings snapshot of {SnapshotType} failed ({Reason}); keeping the last good value"
    )]
    private static partial void LogReloadFailed(
        ILogger logger,
        Exception exception,
        string snapshotType,
        SettingsSnapshotReloadReason reason
    );

    private sealed record State(T Value, long Revision, FrozenDictionary<string, string?> Raw);

    private sealed class Subscription(SettingsSnapshot<T> owner, Action<T, long> listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._Unsubscribe(listener);
            }
        }
    }
}
