// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Settings.Testing;

/// <summary>
/// In-memory <see cref="ISettingsSnapshot{T}"/> for tests: the test sets the value directly instead of loading it from
/// a settings store. Register it in place of the snapshot the application adds with <c>AddSettingsSnapshot&lt;T&gt;</c>,
/// then call <see cref="Set"/> to simulate a settings change.
/// </summary>
/// <remarks>
/// <para>
/// It follows the snapshot contract a consumer depends on: <see cref="TryGetCurrent"/> returns
/// <see langword="false"/> and <see cref="Revision"/> is <c>0</c> until the first value, each <see cref="Set"/> advances
/// the revision by one, and a listener sees the new value and revision already published.
/// </para>
/// <para>
/// Two behaviors differ on purpose. <see cref="GetAsync"/> throws before any value is set instead of waiting for a load
/// that a test double never performs, so a test that forgot to set a value fails at once rather than hanging. A
/// listener exception is rethrown to the caller of <see cref="Set"/> after every listener has run, where the real
/// snapshot logs it, so the test sees the failure.
/// </para>
/// </remarks>
/// <typeparam name="T">The type the settings are bound to.</typeparam>
[PublicAPI]
public sealed class TestSettingsSnapshot<T> : ISettingsSnapshot<T>
{
    private readonly Lock _lock = new();
    private readonly List<Action<T, long>> _listeners = [];
    private T? _value;
    private bool _hasValue;
    private long _revision;

    /// <summary>Creates a snapshot that has not loaded yet; call <see cref="Set"/> to give it a value.</summary>
    public TestSettingsSnapshot() { }

    /// <summary>Creates a snapshot already loaded with <paramref name="value"/> at revision <c>1</c>.</summary>
    /// <param name="value">The initial value.</param>
    public TestSettingsSnapshot(T value)
    {
        _value = value;
        _hasValue = true;
        _revision = 1;
    }

    /// <inheritdoc />
    public long Revision
    {
        get
        {
            lock (_lock)
            {
                return _revision;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetCurrent([MaybeNullWhen(false)] out T value)
    {
        lock (_lock)
        {
            value = _value;
            return _hasValue;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No value has been set.</exception>
    public ValueTask<T> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return TryGetCurrent(out var value)
            ? ValueTask.FromResult(value)
            : ValueTask.FromException<T>(
                new InvalidOperationException(
                    $"The test settings snapshot for '{typeof(T).Name}' has no value. Call Set(...) or pass a value to the constructor before reading it."
                )
            );
    }

    /// <inheritdoc />
    public IDisposable OnChange(Action<T, long> listener)
    {
        Argument.IsNotNull(listener);

        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return DisposableFactory.Create(() =>
        {
            lock (_lock)
            {
                _listeners.Remove(listener);
            }
        });
    }

    /// <summary>
    /// Publishes <paramref name="value"/> as the current value, advances <see cref="Revision"/> by one, and then calls
    /// every registered listener with the new value and revision.
    /// </summary>
    /// <param name="value">The new value.</param>
    /// <returns>The new revision.</returns>
    /// <exception cref="AggregateException">One or more listeners threw; every listener still ran.</exception>
    public long Set(T value)
    {
        long revision;
        Action<T, long>[] listeners;

        lock (_lock)
        {
            _value = value;
            _hasValue = true;
            revision = ++_revision;
            listeners = [.. _listeners];
        }

        // Listeners run outside the lock so one that reads the snapshot or registers another listener cannot deadlock.
        List<Exception>? failures = null;

        foreach (var listener in listeners)
        {
            try
            {
                listener(value, revision);
            }
#pragma warning disable CA1031 // A test double must run every listener before reporting, so it collects any failure.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(exception);
            }
        }

        return failures is null
            ? revision
            : throw new AggregateException("One or more settings snapshot listeners threw.", failures);
    }
}
