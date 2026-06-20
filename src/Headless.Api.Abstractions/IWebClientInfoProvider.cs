// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

public interface IWebClientInfoProvider
{
    /// <summary>Get IpAddress.</summary>
    string? IpAddress { get; }

    /// <summary>Get UserAgent.</summary>
    string? UserAgent { get; }

    /// <summary>Get DeviceInfo.</summary>
    string? DeviceInfo { get; }
}
