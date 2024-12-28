// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class DateTimeOffsetExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ToEgyptTimeZone(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToTimezone(TimezoneConstants.EgyptTimeZone);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ToPalestineTimeZone(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToTimezone(TimezoneConstants.GazaTimeZone);
    }

    [SystemPure]
    [JetBrainsPure]
    public static DateTimeOffset ToSaudiArabiaTimeZone(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToTimezone(TimezoneConstants.SaudiArabiaTimeZone);
    }
}
