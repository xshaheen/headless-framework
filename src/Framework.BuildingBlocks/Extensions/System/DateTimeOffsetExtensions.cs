// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Provides a set of extension methods for operations on <see cref="DateTimeOffset"/>.</summary>
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
