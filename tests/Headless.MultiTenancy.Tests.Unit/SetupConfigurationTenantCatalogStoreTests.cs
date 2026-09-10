// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupConfigurationTenantCatalogStoreTests : TestBase
{
    [Fact]
    public async Task should_resolve_a_working_catalog_service_end_to_end_through_configuration_storage()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseConfiguration(options =>
                    options.Tenants.Add(
                        new ConfigurationTenantSeed
                        {
                            Id = "ten_1",
                            Identifier = "acme",
                            Name = "Acme",
                            IsEnabled = true,
                        }
                    )
                )
            )
        );
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITenantCatalogService>();

        // when
        var outcome = await service.ResolveAsync("acme", AbortToken);

        // then — identical resolution behavior to U3's in-memory-backed equivalent
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant!.Id.Should().Be("ten_1");
    }

    [Fact]
    public void should_throw_at_store_resolution_when_configuration_seeds_have_duplicate_normalized_identifiers()
    {
        // given — AE10 configuration arm, exercised through the full DI wiring. The registered
        // ConfigurationTenantStoreOptionsValidator (Configure<T,TValidator>, per the Options Pattern
        // convention) fires on first IOptions<T>.Value access — inside the store's own constructor — so
        // the surfaced exception is OptionsValidationException; the plain InvalidOperationException
        // constructor guard (ConfigurationTenantStoreTests) covers direct construction that bypasses DI.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseConfiguration(options =>
                {
                    options.Tenants.Add(new ConfigurationTenantSeed { Id = "ten_1", Identifier = "Acme" });
                    options.Tenants.Add(new ConfigurationTenantSeed { Id = "ten_2", Identifier = " acme " });
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
    public void should_throw_at_startup_when_a_seed_has_an_invalid_identifier_shape()
    {
        // given — bad chars/length: the seed identifier can never be reached by resolution (R21),
        // so the configuration store rejects it eagerly instead of shipping dead configuration.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Catalog(catalog =>
                catalog.UseConfiguration(options =>
                    options.Tenants.Add(new ConfigurationTenantSeed { Id = "ten_1", Identifier = "ac_me!" })
                )
            )
        );

        using var provider = builder.Services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<ITenantStore>();

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*does not match*identifier shape*");
    }

    [Fact]
    public async Task should_bind_extra_properties_from_nested_configuration_keys_and_round_trip_through_resolve()
    {
        // given
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tenants:0:Id"] = "ten_1",
            ["Tenants:0:Identifier"] = "acme",
            ["Tenants:0:Name"] = "Acme",
            ["Tenants:0:IsEnabled"] = "true",
            ["Tenants:0:ExtraProperties:Region"] = "eu-west-1",
            ["Tenants:0:ExtraProperties:Plan"] = "enterprise",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseConfiguration(configuration)));
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITenantCatalogService>();

        // when
        var outcome = await service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant!.ExtraProperties["Region"].Should().Be("eu-west-1");
        outcome.Tenant.ExtraProperties["Plan"].Should().Be("enterprise");
    }

    [Fact]
    public async Task should_not_reflect_configuration_changes_made_after_the_startup_snapshot()
    {
        // given — KTD7: bound once at startup; reload requires a process restart.
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tenants:0:Id"] = "ten_1",
            ["Tenants:0:Identifier"] = "acme",
            ["Tenants:0:Name"] = "Acme",
            ["Tenants:0:IsEnabled"] = "true",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseConfiguration(configuration)));

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();

        // Force the IOptions<T> snapshot to be captured before mutating the source.
        var before = await store.FindByIdAsync("ten_1", AbortToken);
        before!.Name.Should().Be("Acme");

        // when — mutate the live configuration source after the snapshot was captured.
        configuration["Tenants:0:Name"] = "Renamed";
        configuration.Reload();

        var after = await store.FindByIdAsync("ten_1", AbortToken);

        // then — the resolved singleton store's snapshot is unaffected.
        after!.Name.Should().Be("Acme");
    }

    [Fact]
    public async Task should_bind_from_a_scoped_configuration_section_via_the_iconfiguration_overload()
    {
        // given
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Headless:MultiTenancy:Tenants:0:Id"] = "ten_1",
            ["Headless:MultiTenancy:Tenants:0:Identifier"] = "acme",
            ["Headless:MultiTenancy:Tenants:0:Name"] = "Acme",
            ["Headless:MultiTenancy:Tenants:0:IsEnabled"] = "true",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var section = configuration.GetSection("Headless:MultiTenancy");

        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Catalog(catalog => catalog.UseConfiguration(section)));
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
    public void should_throw_when_catalog_is_configured_with_use_configuration_and_use_in_memory_together()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        var act = () =>
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Catalog(catalog =>
                {
                    catalog.UseConfiguration(_ => { });
                    catalog.UseInMemory(_ => { });
                })
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*");
    }
}
