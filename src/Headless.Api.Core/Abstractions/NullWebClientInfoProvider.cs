// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Headless.Api.Abstractions;

/// <summary>
/// No-op implementation of <see cref="IWebClientInfoProvider"/> for use in contexts where
/// there is no HTTP request (e.g., background workers, hosted services). All properties
/// return <see langword="null"/>.
/// </summary>
internal sealed class NullWebClientInfoProvider : IWebClientInfoProvider
{
    public static readonly NullWebClientInfoProvider Instance = new();

    private NullWebClientInfoProvider() { }

    public string? IpAddress => null;

    public string? UserAgent => null;

    public ValueTask<string?> GetDeviceInfoAsync(CancellationToken cancellationToken = default) => new((string?)null);
}
