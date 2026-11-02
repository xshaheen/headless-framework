// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Adds <see cref="TenantOutputCachePolicy"/> to the output-cache base policies.</summary>
internal sealed class TenantOutputCacheOptionsSetup(TenantOutputCachePolicy policy)
    : IConfigureOptions<OutputCacheOptions>
{
    public void Configure(OutputCacheOptions options)
    {
        options.AddBasePolicy(policy);
    }
}
