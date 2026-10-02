// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings.Values;

namespace Headless.Settings;

/// <summary>
/// Configures one <see cref="ISettingsSnapshot{T}"/>: the Global settings it is bound from, how they bind to
/// <typeparamref name="T"/>, and how often it is re-read as a backstop.
/// </summary>
/// <typeparam name="T">The type the settings are bound to.</typeparam>
[PublicAPI]
public sealed class SettingsSnapshotBuilder<T>
{
    /// <summary>The backstop re-read interval used when <see cref="Backstop"/> is not called: one minute.</summary>
    public static readonly TimeSpan DefaultBackstop = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest backstop interval accepted: 30 days. A timer cannot wait much past 49 days, and a longer interval
    /// plus its jitter would fault the backstop loop and stop the host.
    /// </summary>
    public static readonly TimeSpan MaxBackstop = TimeSpan.FromDays(30);

    private readonly List<string> _names = [];
    private Func<IReadOnlyDictionary<string, string?>, T>? _bind;
    private TimeSpan _backstop = DefaultBackstop;

    internal SettingsSnapshotBuilder() { }

    /// <summary>Adds the names of the settings the snapshot is bound from. Repeated calls add to the set.</summary>
    /// <param name="names">Setting names. Each must be a defined setting, or every load throws naming the undefined ones.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> or one of its items is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is empty or whitespace.</exception>
    public SettingsSnapshotBuilder<T> Names(params string[] names)
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            _names.Add(Argument.IsNotNullOrWhiteSpace(name));
        }

        return this;
    }

    /// <summary>Sets how the resolved values bind to <typeparamref name="T"/>.</summary>
    /// <param name="bind">
    /// Receives every tracked name mapped to its resolved value, <see langword="null"/> when the setting has no value
    /// and no default. Runs only when a value changed. An exception it throws on the first load reaches the
    /// <c>GetAsync</c> caller and is retried; afterwards it is logged and the snapshot keeps its last good value.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>.</exception>
    public SettingsSnapshotBuilder<T> Bind(Func<IReadOnlyDictionary<string, string?>, T> bind)
    {
        _bind = Argument.IsNotNull(bind);

        return this;
    }

    /// <summary>
    /// Sets how often the snapshot is re-read when no change announcement arrives, for example when the host runs
    /// without messaging or an announcement was lost. Each process jitters it by ±10%. Defaults to
    /// <see cref="DefaultBackstop"/>.
    /// </summary>
    /// <param name="interval">The re-read interval.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is not positive or exceeds <see cref="MaxBackstop"/>.
    /// </exception>
    public SettingsSnapshotBuilder<T> Backstop(TimeSpan interval)
    {
        _backstop = Argument.IsLessThanOrEqualTo(Argument.IsPositive(interval), MaxBackstop);

        return this;
    }

    internal (
        IReadOnlyCollection<string> Names,
        Func<IReadOnlyDictionary<string, string?>, T> Bind,
        TimeSpan Backstop
    ) Build()
    {
        if (_names.Count == 0)
        {
            throw new InvalidOperationException(
                $"The settings snapshot of {typeof(T).Name} names no settings. Call Names(...) with at least one setting name."
            );
        }

        if (_bind is null)
        {
            throw new InvalidOperationException(
                $"The settings snapshot of {typeof(T).Name} has no bind function. Call Bind(...)."
            );
        }

        return (_names, _bind, _backstop);
    }
}
