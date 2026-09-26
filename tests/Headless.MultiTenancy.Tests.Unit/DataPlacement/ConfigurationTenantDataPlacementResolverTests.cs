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
        var a = await sut.ResolveAsync("tenant-a", AbortToken);
        var b = await sut.ResolveAsync("tenant-b", AbortToken);
        var unknown = await sut.ResolveAsync("tenant-c", AbortToken);

        // then
        a!.Schema.Should().Be("tenant_a");
        a.ConnectionString.Should().BeNull();
        b!.Schema.Should().BeNull();
        b.ConnectionString.Should().Be("Host=db;Database=tenant_b");
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task should_compare_tenant_ids_ordinally()
    {
        var sut = _Create(new ConfigurationTenantDataPlacement { TenantId = "Tenant-A", Schema = "tenant_a" });

        (await sut.ResolveAsync("tenant-a", AbortToken)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void should_reject_invalid_options(ConfigurationTenantDataPlacementOptions options)
    {
        var result = new ConfigurationTenantDataPlacementOptionsValidator().Validate(options);

        result.IsValid.Should().BeFalse();
    }

    public static TheoryData<ConfigurationTenantDataPlacementOptions> InvalidOptions() =>
        new(
            new ConfigurationTenantDataPlacementOptions
            {
                Tenants =
                [
                    new() { TenantId = "tenant-a", Schema = "a" },
                    new() { TenantId = "tenant-a", Schema = "b" },
                ],
            },
            new ConfigurationTenantDataPlacementOptions { Tenants = [new() { TenantId = "", Schema = "a" }] },
            new ConfigurationTenantDataPlacementOptions { Tenants = [new() { TenantId = "tenant-a" }] },
            new ConfigurationTenantDataPlacementOptions { Tenants = [new() { TenantId = "tenant-a", Schema = " " }] },
            new ConfigurationTenantDataPlacementOptions
            {
                Tenants = [new() { TenantId = "tenant-a", ConnectionString = "" }],
            }
        );

    [Fact]
    public void should_accept_valid_options()
    {
        var options = new ConfigurationTenantDataPlacementOptions
        {
            Tenants =
            [
                new() { TenantId = "tenant-a", Schema = "a" },
                new() { TenantId = "tenant-b", ConnectionString = "Host=db" },
            ],
        };

        new ConfigurationTenantDataPlacementOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }

    private static ConfigurationTenantDataPlacementResolver _Create(params ConfigurationTenantDataPlacement[] entries)
    {
        return new ConfigurationTenantDataPlacementResolver(
            Options.Create(new ConfigurationTenantDataPlacementOptions { Tenants = entries })
        );
    }
}
