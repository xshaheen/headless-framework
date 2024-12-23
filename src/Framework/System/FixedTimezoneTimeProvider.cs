// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.System;

public sealed class FixedTimezoneTimeProvider(TimeZoneInfo timeZone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => timeZone;
}
