// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public sealed class FixedTimezoneTimeProvider(TimeZoneInfo timeZone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => timeZone;
}
