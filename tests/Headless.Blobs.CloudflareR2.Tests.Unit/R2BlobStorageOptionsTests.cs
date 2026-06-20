// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.CloudflareR2;
using Headless.Testing.Tests;

namespace Tests;

public sealed class R2BlobStorageOptionsTests : TestBase
{
    [Fact]
    public void default_jurisdiction_uses_global_endpoint()
    {
        var options = new R2BlobStorageOptions { AccountId = "acc123" };

        options.GetEffectiveEndpointUrl().Should().Be("https://acc123.r2.cloudflarestorage.com");
    }

    [Fact]
    public void eu_jurisdiction_uses_eu_endpoint()
    {
        var options = new R2BlobStorageOptions { AccountId = "acc123", Jurisdiction = R2Jurisdiction.EuropeanUnion };

        options.GetEffectiveEndpointUrl().Should().Be("https://acc123.eu.r2.cloudflarestorage.com");
    }

    [Fact]
    public void fedramp_jurisdiction_uses_fedramp_endpoint()
    {
        var options = new R2BlobStorageOptions { AccountId = "acc123", Jurisdiction = R2Jurisdiction.FedRamp };

        options.GetEffectiveEndpointUrl().Should().Be("https://acc123.fedramp.r2.cloudflarestorage.com");
    }

    [Fact]
    public void explicit_endpoint_url_overrides_jurisdiction()
    {
        var options = new R2BlobStorageOptions
        {
            AccountId = "acc123",
            Jurisdiction = R2Jurisdiction.EuropeanUnion,
            EndpointUrl = "https://{0}.custom.example.com",
        };

        options.GetEffectiveEndpointUrl().Should().Be("https://acc123.custom.example.com");
    }
}
