// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging.Internal;

/// <summary>
/// Exposes the state of the entry the current thread is logging to the enrichers that run inside that call.
/// </summary>
/// <remarks>
/// Microsoft's enrichment hands an enricher only a collector, and it keeps a tag whose key the entry's own state already
/// has, which repeats the key on the entry and makes it invalid OTLP. The redacting logger is the outermost logger of a
/// Headless host and calls Microsoft's one synchronously on the same thread, so it opens the entry here first and an
/// enricher can skip a key the entry already carries.
/// </remarks>
internal static class LoggedEntry
{
    [ThreadStatic]
    private static Probe? _current;

    /// <summary>
    /// Whether the entry being logged on this thread has a property named <paramref name="key"/>; <see langword="false"/>
    /// outside a logging call.
    /// </summary>
    public static bool HasProperty(string key) => _current?.HasProperty(key) ?? false;

    /// <summary>Opens <paramref name="state"/> as the entry being logged until the returned scope is disposed.</summary>
    public static Scope Enter<TState>(TState state)
    {
        var previous = _current;
        var probe = Probe<TState>.Rent(state);
        _current = probe;

        return new Scope(probe, previous);
    }

    internal readonly struct Scope(Probe probe, Probe? previous) : IDisposable
    {
        public void Dispose()
        {
            probe.Release();
            _current = previous;
        }
    }

    internal abstract class Probe
    {
        public abstract bool HasProperty(string key);

        public abstract void Release();
    }

    // Generic, so a struct state is held without boxing on every entry; it boxes only when an enricher asks.
    private sealed class Probe<TState> : Probe
    {
        [ThreadStatic]
        private static Probe<TState>? _cached;

        private TState? _state;

        private bool _inUse;

        public static Probe<TState> Rent(TState state)
        {
            // A provider that logs while it handles an entry nests a second call on this thread; that one gets its own
            // probe so the outer entry is still readable once it returns.
            var cached = _cached;
            var probe = cached?._inUse == false ? cached : (_cached = new Probe<TState>());
            probe._inUse = true;
            probe._state = state;

            return probe;
        }

        public override bool HasProperty(string key)
        {
            if (_state is not IReadOnlyList<KeyValuePair<string, object?>> properties)
            {
                return false;
            }

            for (var i = 0; i < properties.Count; i++)
            {
                if (string.Equals(properties[i].Key, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // The state can be a generator's thread-local instance or hold the entry's raw values; let go of it.
        public override void Release()
        {
            _state = default;
            _inUse = false;
        }
    }
}
