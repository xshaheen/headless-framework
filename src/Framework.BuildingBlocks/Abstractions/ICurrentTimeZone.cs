// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Abstractions;

public interface ICurrentTimeZone
{
    /// <summary>Gets the current time zone.</summary>
    TimeZoneInfo TimeZone { get; }
}

public sealed class LocalCurrentTimeZone : ICurrentTimeZone
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;
}
