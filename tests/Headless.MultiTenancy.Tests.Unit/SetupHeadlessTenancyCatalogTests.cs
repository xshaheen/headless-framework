// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupHeadlessTenancyCatalogTests : TestBase
{
    [Fact]
    public void should_throw_when_catalog_is_configured_with_no_storage_provider()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        var act = () => builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(_ => { }));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*");
    }

    [Fact]
    public void should_throw_when_catalog_is_configured_with_two_storage_providers()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Catalog(catalog =>
                {
                    catalog.UseInMemory(_ => { });
                    catalog.UseInMemory(_ => { });
                })
            );

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_record_accessor_capability_when_a_store_is_configured()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseInMemory(_ => { })));

        // then
        var manifest = builder.Services.GetOrAddTenantPostureManifest();
        var seam = manifest.GetSeam(TenantCatalogPosture.Seam);
        seam.Should().NotBeNull();
        seam!.Capabilities.Should().Contain(TenantCatalogPosture.AccessorCapability);
    }

    [Fact]
    public async Task should_replace_the_default_null_accessor_with_the_catalog_backed_accessor()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(options =>
                    options.Tenants.Add(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true))
                )
            )
        );
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        // A real host wires ambient tenant context through Api.Core/Jobs.Core/Messaging.Core (each of
        // which references Headless.Core); Headless.MultiTenancy cannot register it itself (U1's
        // no-cycle constraint), so this test simulates that wiring directly.
        builder.Services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();

        await using var provider = builder.Services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // when
        var accessor = scope.ServiceProvider.GetRequiredService<ICurrentTenantInfo>();

        // then
        accessor.Should().BeOfType<TenantCatalogCurrentTenantInfo>();
    }

    [Fact]
    public async Task should_resolve_a_working_catalog_service_end_to_end_through_in_memory_storage()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(options =>
                    options.Tenants.Add(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true))
                )
            )
        );
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

        await using var provider = builder.Services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITenantCatalogService>();

        // when
        var outcome = await service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant!.Id.Should().Be("ten_1");
    }

    [Fact]
    public void should_throw_at_store_resolution_when_in_memory_seeds_have_duplicate_normalized_identifiers()
    {
        // given — AE10 in-memory arm, exercised through the full DI wiring. The registered
        // InMemoryTenantStoreOptionsValidator (Configure<T,TValidator>, per the Options Pattern
        // convention) fires on first IOptions<T>.Value access — inside the store's own constructor —
        // so the surfaced exception is OptionsValidationException; the plain InvalidOperationException
        // constructor guard (InMemoryTenantStoreTests) covers direct construction that bypasses DI.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(options =>
                {
                    options.Tenants.Add(new TenantInfo("ten_1", "Acme", "Acme", isEnabled: true));
                    options.Tenants.Add(new TenantInfo("ten_2", " acme ", "Acme Duplicate", isEnabled: true));
                })
            )
        );

        using var provider = builder.Services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<ITenantStore>();

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*normalize to the same identifier*");
    }

    [Fact]
    public async Task should_enumerate_seeded_tenants_through_the_directory_capability()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(options =>
                {
                    options.Tenants.Add(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));
                    options.Tenants.Add(new TenantInfo("ten_2", "globex", "Globex", isEnabled: false));
                })
            )
        );

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // when
        var directory = scope.ServiceProvider.GetRequiredService<ITenantDirectory>();
        var all = await directory.GetAllAsync(AbortToken);

        // then
        all.Should().HaveCount(2);
    }
}
