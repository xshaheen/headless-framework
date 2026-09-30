// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.DataPlacement;

public sealed class ConfigurationTenantDataPlacementResolverTests : TestBase
{
    [Fact]
    public async Task should_return_configured_placement_and_null_for_unknown_tenant()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Placement:Tenants:0:TenantId"] = "tenant-a",
                    ["Placement:Tenants:0:Schema"] = "tenant_a",
                    ["Placement:Tenants:1:TenantId"] = "tenant-b",
                    ["Placement:Tenants:1:ConnectionString"] = "Host=db;Database=tenant_b",
                    ["Placement:Tenants:2:TenantId"] = "tenant-b",
                    ["Placement:Tenants:2:DataStore"] = "billing",
                    ["Placement:Tenants:2:Schema"] = "tenant_b_billing",
                    ["Placement:Tenants:3:TenantId"] = "tenant-s",
                    ["Placement:Tenants:3:Shared"] = "true",
                }
            )
            .Build();
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.DataPlacement(p => p.UseConfiguration(configuration.GetSection("Placement")))
        );
        await using var provider = builder.Services.BuildServiceProvider();
        var sut = provider.GetRequiredService<ITenantDataPlacementResolver>();

        // when
        var a = await sut.ResolveAsync(new("tenant-a"), AbortToken);
        var b = await sut.ResolveAsync(new("tenant-b"), AbortToken);
        var bBilling = await sut.ResolveAsync(new("tenant-b", "billing"), AbortToken);
        var aBilling = await sut.ResolveAsync(new("tenant-a", "billing"), AbortToken);
        var shared = await sut.ResolveAsync(new("tenant-s"), AbortToken);
        var unknown = await sut.ResolveAsync(new("tenant-c"), AbortToken);

        // then
        a!.Schema.Should().Be("tenant_a");
        a.ConnectionString.Should().BeNull();
        b!.Schema.Should().BeNull();
        b.ConnectionString.Should().Be("Host=db;Database=tenant_b");
        bBilling!.Schema.Should().Be("tenant_b_billing");
        aBilling.Should().BeNull("a data store the tenant has no entry for is unplaced, not the default store");
        shared.Should().BeSameAs(TenantDataPlacement.Shared);
        unknown.Should().BeNull();
        provider.GetRequiredService<ITenantDataPlacementCacheInvalidator>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_compare_tenant_ids_ordinally()
    {
        var sut = _Create(new ConfigurationTenantDataPlacement { TenantId = "Tenant-A", Schema = "tenant_a" });

        (await sut.ResolveAsync(new("tenant-a"), AbortToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void should_reject_invalid_options(int caseIndex)
    {
        var result = new ConfigurationTenantDataPlacementOptionsValidator().Validate(_InvalidOptions()[caseIndex]);

        result.IsValid.Should().BeFalse();
    }

    private static ConfigurationTenantDataPlacementOptions[] _InvalidOptions() =>
        [
            new()
            {
                Tenants =
                [
                    new() { TenantId = "tenant-a", Schema = "a" },
                    new() { TenantId = "tenant-a", Schema = "b" },
                ],
            },
            new() { Tenants = [new() { TenantId = "", Schema = "a" }] },
            new() { Tenants = [new() { TenantId = "tenant-a" }] },
            new() { Tenants = [new() { TenantId = "tenant-a", Schema = " " }] },
            new() { Tenants = [new() { TenantId = "tenant-a", ConnectionString = "" }] },
            new()
            {
                Tenants =
                [
                    new()
                    {
                        TenantId = "tenant-a",
                        Shared = true,
                        Schema = "a",
                    },
                ],
            },
            new()
            {
                Tenants =
                [
                    new()
                    {
                        TenantId = "tenant-a",
                        DataStore = " ",
                        Schema = "a",
                    },
                ],
            },
            new()
            {
                Tenants =
                [
                    new()
                    {
                        TenantId = "tenant-a",
                        DataStore = "orders",
                        Schema = "a",
                    },
                    new()
                    {
                        TenantId = "tenant-a",
                        DataStore = "orders",
                        Shared = true,
                    },
                ],
            },
        ];

    [Fact]
    public void should_accept_valid_options()
    {
        var options = new ConfigurationTenantDataPlacementOptions
        {
            Tenants =
            [
                new() { TenantId = "tenant-a", Schema = "a" },
                new()
                {
                    TenantId = "tenant-a",
                    DataStore = "orders",
                    Schema = "a_orders",
                },
                new() { TenantId = "tenant-b", ConnectionString = "Host=db" },
                new() { TenantId = "tenant-s", Shared = true },
            ],
        };

        new ConfigurationTenantDataPlacementOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_non_positive_cache_expiration(int seconds)
    {
        var options = new TenantDataPlacementOptions { CacheExpiration = TimeSpan.FromSeconds(seconds) };

        new TenantDataPlacementOptionsValidator().Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_accept_a_positive_cache_expiration()
    {
        new TenantDataPlacementOptionsValidator().Validate(new TenantDataPlacementOptions()).IsValid.Should().BeTrue();
    }

    private static ConfigurationTenantDataPlacementResolver _Create(params ConfigurationTenantDataPlacement[] entries)
    {
        return new ConfigurationTenantDataPlacementResolver(
            Options.Create(new ConfigurationTenantDataPlacementOptions { Tenants = entries })
        );
    }
}
