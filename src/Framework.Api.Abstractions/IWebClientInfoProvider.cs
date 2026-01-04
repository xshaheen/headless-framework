// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Abstractions;

public interface IWebClientInfoProvider
{
    /// <summary>Get IpAddress.</summary>
    string? IpAddress { get; }

    /// <summary>Get UserAgent.</summary>
    string? UserAgent { get; }

    /// <summary>Get DeviceInfo.</summary>
    string? DeviceInfo { get; }
}

public sealed class NullWebClientInfoProvider : IWebClientInfoProvider
{
    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? DeviceInfo => null;
}
