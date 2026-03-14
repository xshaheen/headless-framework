// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Headless.Core;

public sealed class ChangeableTimezoneTimeProvider(TimeZoneInfo defaultTimeZone) : TimeProvider
{
    private TimeZoneInfo _currentLocalTimeZone = defaultTimeZone;

    public override TimeZoneInfo LocalTimeZone => _currentLocalTimeZone;

    [MustDisposeResource]
    public IDisposable ChangeTimeZone(TimeZoneInfo newTimeZone)
    {
        var previousTimeZone = _currentLocalTimeZone;
        _currentLocalTimeZone = newTimeZone;
        return new Disposable(() => _currentLocalTimeZone = previousTimeZone);
    }
}
