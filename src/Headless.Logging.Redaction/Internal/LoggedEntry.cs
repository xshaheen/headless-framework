// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

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

    // Constrained to the list, so a struct state's Count and indexer are called on it in place, without a box.
    private static bool _HasKey<TList>(ref TList properties, string key)
        where TList : IReadOnlyList<KeyValuePair<string, object?>>
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (string.Equals(properties[i].Key, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal abstract class Probe
    {
        public abstract bool HasProperty(string key);

        public abstract void Release();
    }

    // Generic, so a struct state is held, and its keys are read, without boxing it.
    private sealed class Probe<TState> : Probe
    {
        // A struct state, such as the one LoggerMessage.Define, [LoggerMessage], or a message template logs with, is
        // read through a method constrained to it, which calls the list members without boxing the struct; casting it
        // to the interface would box it on every entry the tenant enricher reads.
        private static readonly KeyReader? _StructReader = _CreateStructReader();

        [ThreadStatic]
        private static Probe<TState>? _cached;

        private TState? _state;

        private bool _inUse;

        private delegate bool KeyReader(ref TState state, string key);

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
            if (typeof(TState).IsValueType)
            {
                return _StructReader?.Invoke(ref _state!, key) == true;
            }

            // A reference state is already an object; the type test allocates nothing.
            return _state is IReadOnlyList<KeyValuePair<string, object?>> properties && _HasKey(ref properties, key);
        }

        private static KeyReader? _CreateStructReader()
        {
            if (
                !typeof(TState).IsValueType
                || !typeof(IReadOnlyList<KeyValuePair<string, object?>>).IsAssignableFrom(typeof(TState))
            )
            {
                return null;
            }

            try
            {
#pragma warning disable REFL031 // False positive: the guard above returns unless TState implements the constrained list.
                return typeof(LoggedEntry)
                    .GetMethod(
                        nameof(_HasKey),
                        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
                    )!
                    .MakeGenericMethod(typeof(TState))
                    .CreateDelegate<KeyReader>();
#pragma warning restore REFL031
            }
            catch (NotSupportedException)
            {
                // Native AOT can refuse a generic instantiation it did not compile ahead of time; boxing still reads
                // the keys correctly, and logging must never fail over it.
                return static (ref state, key) =>
                    state is IReadOnlyList<KeyValuePair<string, object?>> properties && _HasKey(ref properties, key);
            }
        }

        // The state can be a generator's thread-local instance or hold the entry's raw values; let go of it.
        public override void Release()
        {
            _state = default;
            _inUse = false;
        }
    }
}
