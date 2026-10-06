// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Fallback <see cref="IUserAgentParser"/> that identifies nothing, so <see cref="IWebClientInfoProvider.DeviceInfo"/>
/// stays <see langword="null"/> until the host installs a real parser such as <c>Headless.Api.UserAgent</c>.
/// </summary>
internal sealed class NullUserAgentParser : IUserAgentParser
{
    public static readonly NullUserAgentParser Instance = new();

    private NullUserAgentParser() { }

    public UserAgentInfo? Parse(string? userAgent) => null;
}
