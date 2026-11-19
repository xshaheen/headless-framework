// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>Reads <see cref="HeadlessPrefixOptions.ConfigurationKey" /> into the host's prefix.</summary>
/// <remarks>
/// It is registered ahead of every other configuration of <see cref="HeadlessPrefixOptions" />, so a
/// <c>ConfigureHeadlessPrefix</c> call in code wins over configuration whichever is registered first. The key is read
/// directly instead of through the reflection binder, which keeps the package trimming-safe.
/// </remarks>
internal sealed class HeadlessPrefixConfiguration(IConfiguration? configuration)
    : IConfigureOptions<HeadlessPrefixOptions>
{
    public void Configure(HeadlessPrefixOptions options)
    {
        if (configuration?[HeadlessPrefixOptions.ConfigurationKey] is { } prefix)
        {
            options.Prefix = prefix;
        }
    }
}
