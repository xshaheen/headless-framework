// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class DateTimeOffsetExtensions
{
    extension(DateTimeOffset dateTimeOffset)
    {
        [SystemPure]
        [JetBrainsPure]
        public DateTimeOffset ToEgyptTimeZone()
        {
            return dateTimeOffset.ToTimezone(TimezoneConstants.EgyptTimeZone);
        }

        [SystemPure]
        [JetBrainsPure]
        public DateTimeOffset ToPalestineTimeZone()
        {
            return dateTimeOffset.ToTimezone(TimezoneConstants.GazaTimeZone);
        }

        [SystemPure]
        [JetBrainsPure]
        public DateTimeOffset ToSaudiArabiaTimeZone()
        {
            return dateTimeOffset.ToTimezone(TimezoneConstants.SaudiArabiaTimeZone);
        }
    }
}
