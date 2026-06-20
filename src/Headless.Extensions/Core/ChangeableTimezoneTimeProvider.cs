// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Headless.Core;

/// <summary>
/// A <see cref="TimeProvider"/> whose <see cref="LocalTimeZone"/> can be overridden at runtime via
/// <see cref="ChangeTimeZone"/>, primarily for testing or request-scoped timezone control.
/// </summary>
/// <param name="defaultTimeZone">The initial value of <see cref="LocalTimeZone"/> until it is changed.</param>
/// <remarks>
/// A single writer is expected: <see cref="ChangeTimeZone"/> returns an <see cref="IDisposable"/> that restores the
/// previous zone, so nested or concurrent calls are not supported and would restore out of order.
/// </remarks>
public sealed class ChangeableTimezoneTimeProvider(TimeZoneInfo defaultTimeZone) : TimeProvider
{
    // volatile: LocalTimeZone is read on arbitrary threads while ChangeTimeZone writes from another.
    // Single-writer expected (the returned IDisposable restores the prior zone; nested/concurrent
    // ChangeTimeZone calls are not supported and would restore out of order).
    private volatile TimeZoneInfo _currentLocalTimeZone = defaultTimeZone;

    /// <summary>Gets the local time zone currently in effect, which may have been overridden by <see cref="ChangeTimeZone"/>.</summary>
    public override TimeZoneInfo LocalTimeZone => _currentLocalTimeZone;

    /// <summary>Overrides <see cref="LocalTimeZone"/> with <paramref name="newTimeZone"/> until the returned scope is disposed.</summary>
    /// <param name="newTimeZone">The time zone to use as <see cref="LocalTimeZone"/> for the lifetime of the returned scope.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previously effective time zone when disposed.</returns>
    [MustDisposeResource]
    public IDisposable ChangeTimeZone(TimeZoneInfo newTimeZone)
    {
        var previousTimeZone = _currentLocalTimeZone;
        _currentLocalTimeZone = newTimeZone;
        return new Disposable(() => _currentLocalTimeZone = previousTimeZone);
    }
}
