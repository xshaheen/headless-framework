// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class ApiSetupTests
{
    [Fact]
    public void add_headless_api_should_register_current_tenant_by_default()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadlessApi(_ConfigureEncryption);

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        currentTenant.Id.Should().BeNull();
        currentTenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void add_headless_multi_tenancy_should_replace_current_tenant_and_store_custom_claim_type()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        builder.AddHeadlessMultiTenancy(options => options.ClaimType = "custom_tenant_id");

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();
        var options = serviceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        options.ClaimType.Should().Be("custom_tenant_id");
    }

    private static void _ConfigureEncryption(StringEncryptionOptions options)
    {
        options.DefaultPassPhrase = "TestPassPhrase123456";
        options.InitVectorBytes = "TestIV0123456789"u8.ToArray();
        options.DefaultSalt = "TestSalt"u8.ToArray();
    }
}
