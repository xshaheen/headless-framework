// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Framework.Core;

public sealed class ChangeableTimezoneTimeProvider(TimeZoneInfo defaultTimeZone) : TimeProvider
{
    private TimeZoneInfo _currentLocalTimeZone = defaultTimeZone;

    public override TimeZoneInfo LocalTimeZone => _currentLocalTimeZone;

    public IDisposable ChangeLocalTimeZone(TimeZoneInfo newTimeZone)
    {
        var previousTimeZone = _currentLocalTimeZone;
        _currentLocalTimeZone = newTimeZone;
        return new Disposable(() => _currentLocalTimeZone = previousTimeZone);
    }
}
