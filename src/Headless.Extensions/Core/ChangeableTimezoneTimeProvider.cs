// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Headless.Core;

public sealed class ChangeableTimezoneTimeProvider(TimeZoneInfo defaultTimeZone) : TimeProvider
{
    // volatile: LocalTimeZone is read on arbitrary threads while ChangeTimeZone writes from another.
    // Single-writer expected (the returned IDisposable restores the prior zone; nested/concurrent
    // ChangeTimeZone calls are not supported and would restore out of order).
    private volatile TimeZoneInfo _currentLocalTimeZone = defaultTimeZone;

    public override TimeZoneInfo LocalTimeZone => _currentLocalTimeZone;

    [MustDisposeResource]
    public IDisposable ChangeTimeZone(TimeZoneInfo newTimeZone)
    {
        var previousTimeZone = _currentLocalTimeZone;
        _currentLocalTimeZone = newTimeZone;
        return new Disposable(() => _currentLocalTimeZone = previousTimeZone);
    }
}
