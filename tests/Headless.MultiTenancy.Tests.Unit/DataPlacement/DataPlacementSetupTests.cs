// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting.DependencyInjection;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.DataPlacement;

public sealed class DataPlacementSetupTests : TestBase
{
    [Fact]
    public void should_reject_no_placement_source()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.AddHeadlessTenancy(tenancy => tenancy.DataPlacement(_ => { }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseConfiguration*UseResolver*");
    }

    [Fact]
    public void should_reject_two_placement_sources()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.DataPlacement(p => p.UseConfiguration(_ => { }).UseResolver<StaticResolver>())
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple*");
    }

    [Fact]
    public void should_reject_a_second_data_placement_call()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy
                    .DataPlacement(p => p.UseConfiguration(_ => { }))
                    .DataPlacement(p => p.UseConfiguration(_ => { }))
            );

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_record_the_data_placement_seam()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddHeadlessTenancy(tenancy => tenancy.DataPlacement(p => p.UseConfiguration(_ => { })));

        var seam = builder.Services.GetOrAddTenantPostureManifest().GetSeam(SetupHeadlessTenancyDataPlacement.Seam);
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Configured);
        seam.Capabilities.Should().Contain("configuration");
    }

    [Fact]
    public async Task should_resolve_a_custom_resolver_through_the_cache()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());
        builder.AddHeadlessTenancy(tenancy => tenancy.DataPlacement(p => p.UseResolver<StaticResolver>()));
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // when
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantDataPlacementResolver>();
        var placement = await resolver.ResolveAsync("tenant-a", AbortToken);

        // then
        resolver.Should().BeOfType<CachingTenantDataPlacementResolver<StaticResolver>>();
        placement!.Schema.Should().Be("tenant_a");
    }

    [Fact]
    public async Task should_fail_startup_without_an_in_process_cache_for_a_custom_resolver()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.DataPlacement(p => p.UseResolver<StaticResolver>()));
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<MissingRequiredServiceException>()).WithMessage("*IInMemoryCache*");
    }

    private sealed class StaticResolver : ITenantDataPlacementResolver
    {
        public Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TenantDataPlacement?>(new TenantDataPlacement("tenant_a", connectionString: null));
        }
    }
}
