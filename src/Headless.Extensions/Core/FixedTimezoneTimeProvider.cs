// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>
/// A <see cref="TimeProvider"/> that reports a fixed <see cref="LocalTimeZone"/> regardless of the host machine's
/// configured time zone. Useful for deterministic, timezone-stable behavior in tests and services.
/// </summary>
/// <param name="timeZone">The time zone to report from <see cref="LocalTimeZone"/>.</param>
public sealed class FixedTimezoneTimeProvider(TimeZoneInfo timeZone) : TimeProvider
{
    /// <summary>Gets the fixed local time zone supplied at construction.</summary>
    public override TimeZoneInfo LocalTimeZone => timeZone;
}
