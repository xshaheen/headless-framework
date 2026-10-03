// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Settings.Values;

/// <summary>
/// A typed value bound from a fixed set of Global settings, held in memory and kept current as those settings change.
/// Register one with <c>AddSettingsSnapshot&lt;T&gt;</c> and inject it wherever the value is read on a hot path.
/// </summary>
/// <remarks>
/// <para>
/// The value is resolved at <see cref="SettingValueProviderNames.Global"/> scope with the usual fallback to the
/// definition default, so a tenant or user override never shadows it. Loading never blocks host startup: the first
/// load runs in the background as soon as the host starts, retrying until it succeeds, and the first
/// <see cref="GetAsync"/> call loads it on demand if it has not finished. The snapshot is then reloaded when a
/// <see cref="SettingChangedMessage"/> names one of its settings, when its every-instance subscription is established,
/// and on a backstop timer.
/// </para>
/// <para>
/// <see cref="Revision"/> moves only when a resolved setting value changes. A reload that reads the same values keeps
/// the same value instance and the same revision, so anything keyed on the revision, such as a
/// rate-limit partition, is not reset by a reload that changed nothing.
/// </para>
/// </remarks>
/// <typeparam name="T">The type the settings are bound to.</typeparam>
[PublicAPI]
public interface ISettingsSnapshot<T>
{
    /// <summary>
    /// Gets the value bound from the settings' current values without waiting, for synchronous code such as a
    /// rate-limiter partition factory.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="false"/> until the first load completes, for example right after startup or while the
    /// settings store is unreachable, so the caller decides what "not loaded yet" means. Code that can await should use
    /// <see cref="GetAsync"/>, which waits for the first load instead.
    /// </remarks>
    /// <param name="value">The current value, when the snapshot has loaded.</param>
    /// <returns><see langword="true"/> when the snapshot has loaded and <paramref name="value"/> holds its value.</returns>
    bool TryGetCurrent([MaybeNullWhen(false)] out T value);

    /// <summary>
    /// Gets the value bound from the settings' current values, loading the snapshot first when it has not loaded yet.
    /// Once loaded this completes synchronously with the current value.
    /// </summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The current value.</returns>
    /// <exception cref="InvalidOperationException">A tracked setting is not defined.</exception>
    /// <remarks>
    /// When the first load fails, for example because the store is unreachable or the bind function rejects a value,
    /// the exception propagates to this caller and the next call tries again.
    /// </remarks>
    ValueTask<T> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the revision of the current value: <c>1</c> after the first load, incremented by one each time a
    /// resolved setting value changes, and <c>0</c> before the first load.
    /// </summary>
    long Revision { get; }

    /// <summary>
    /// Registers a listener called with the new value and revision after each change, once <see cref="TryGetCurrent"/>
    /// and <see cref="Revision"/> already show them.
    /// </summary>
    /// <remarks>
    /// A listener runs on the thread that reloaded the snapshot and must not block. An exception it throws is logged and
    /// does not stop other listeners.
    /// </remarks>
    /// <param name="listener">The callback receiving the new value and its revision.</param>
    /// <returns>A handle that unregisters the listener when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="listener"/> is <see langword="null"/>.</exception>
    IDisposable OnChange(Action<T, long> listener);
}
