// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Values;

/// <summary>
/// A typed value bound from a fixed set of Global settings, held in memory and kept current as those settings change.
/// Register one with <c>AddSettingsSnapshot&lt;T&gt;</c> and inject it wherever the value is read on a hot path.
/// </summary>
/// <remarks>
/// <para>
/// The value is resolved at <see cref="SettingValueProviderNames.Global"/> scope with the usual fallback to the
/// definition default, so a tenant or user override never shadows it. The snapshot is loaded when the host starts and
/// reloaded when a <see cref="SettingChangedMessage"/> names one of its settings, when its every-instance subscription
/// is established, and on a backstop timer.
/// </para>
/// <para>
/// <see cref="Revision"/> moves only when a resolved setting value changes. A reload that reads the same values keeps
/// the same <see cref="Current"/> instance and the same revision, so anything keyed on the revision, such as a
/// rate-limit partition, is not reset by a reload that changed nothing.
/// </para>
/// </remarks>
/// <typeparam name="T">The type the settings are bound to.</typeparam>
[PublicAPI]
public interface ISettingsSnapshot<out T>
{
    /// <summary>Gets the value bound from the settings' current values.</summary>
    /// <exception cref="InvalidOperationException">The snapshot has not been loaded yet; it loads when the host starts.</exception>
    T Current { get; }

    /// <summary>
    /// Gets the revision of <see cref="Current"/>: <c>1</c> after the first load, incremented by one each time a
    /// resolved setting value changes, and <c>0</c> before the first load.
    /// </summary>
    long Revision { get; }

    /// <summary>
    /// Registers a listener called with the new value and revision after each change, once <see cref="Current"/> and
    /// <see cref="Revision"/> already show them.
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
