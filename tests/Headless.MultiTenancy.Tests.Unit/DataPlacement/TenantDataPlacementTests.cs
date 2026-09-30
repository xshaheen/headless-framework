// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.DataPlacement;

public sealed class TenantDataPlacementTests : TestBase
{
    [Theory]
    [InlineData("tenant_a", null)]
    [InlineData(null, "Host=db;Database=tenant_a")]
    [InlineData("tenant_a", "Host=db;Database=tenant_a")]
    public void should_keep_supplied_members(string? schema, string? connectionString)
    {
        var placement = new TenantDataPlacement(schema, connectionString);

        placement.Schema.Should().Be(schema);
        placement.ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public void should_reject_a_placement_with_neither_member()
    {
        var act = () => new TenantDataPlacement(schema: null, connectionString: null);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, "")]
    [InlineData(null, " ")]
    [InlineData("tenant_a", " ")]
    public void should_reject_blank_members(string? schema, string? connectionString)
    {
        var act = () => new TenantDataPlacement(schema, connectionString);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_expose_the_shared_placement_as_a_singleton_without_members()
    {
        var shared = TenantDataPlacement.Shared;

        shared.IsShared.Should().BeTrue();
        shared.Schema.Should().BeNull();
        shared.ConnectionString.Should().BeNull();
        shared.Should().BeSameAs(TenantDataPlacement.Shared);
        shared.ToString().Should().Contain("Shared");
        new TenantDataPlacement("tenant_a", connectionString: null).IsShared.Should().BeFalse();
    }

    [Fact]
    public void should_default_a_request_to_the_default_data_store_and_compare_by_value()
    {
        var request = new TenantDataPlacementRequest("tenant-a");

        request.DataStore.Should().Be(TenantDataPlacementRequest.DefaultDataStore);
        request.Should().Be(new TenantDataPlacementRequest("tenant-a", TenantDataPlacementRequest.DefaultDataStore));
        request.Should().NotBe(new TenantDataPlacementRequest("tenant-a", "orders"));
        request.Should().NotBe(new TenantDataPlacementRequest("Tenant-A"));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData("tenant-a", "")]
    [InlineData("tenant-a", " ")]
    public void should_reject_a_blank_request_member(string tenantId, string? dataStore)
    {
        var act = () => new TenantDataPlacementRequest(tenantId, dataStore);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_not_expose_connection_string_through_to_string()
    {
        const string secret = "Host=db;Database=tenant_a;Password=hunter2";

        var text = new TenantDataPlacement("tenant_a", secret).ToString();

        text.Should().NotContain(secret).And.NotContain("hunter2").And.Contain("tenant_a");
    }

    [Fact]
    public async Task should_resolve_no_placement_by_default()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(_ => { });
        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var placement = await provider
            .GetRequiredService<ITenantDataPlacementResolver>()
            .ResolveAsync(new("tenant-a"), AbortToken);

        // then
        placement.Should().BeNull();
    }
}
