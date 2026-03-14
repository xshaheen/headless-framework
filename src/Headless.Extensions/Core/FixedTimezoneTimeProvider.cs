// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

public sealed class FixedTimezoneTimeProvider(TimeZoneInfo timeZone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => timeZone;
}
