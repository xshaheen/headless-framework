// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Store-conformance suite run by every <see cref="ITenantStore"/> implementation (KTD10): round-trip
/// lookup by normalized identifier and by canonical id, unknown-identifier/unknown-id misses,
/// duplicate-identifier rejection (R20), enumeration including disabled tenants (R4), disabled-tenant
/// surfacing without rejection (R9 — rejection is a resolution-time, catalog-service concern, never a
/// store concern), and <see cref="TenantInfo.ExtraProperties"/> round-trip. Also covers the store-level
/// half of R7: stores compare the normalized identifier ordinally and never re-normalize —
/// normalization-equivalence itself (e.g. that <c>ACME</c> and <c>acme</c> resolve to the same tenant) is
/// a catalog-<em>service</em> behavior, tested in <c>Headless.MultiTenancy.Tests.Unit</c>, not here.
/// </summary>
/// <typeparam name="TFixture">The leaf fixture that owns this store's backing resource.</typeparam>
public abstract class TenantStoreConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, ITenantCatalogStoreFixture
{
    [Fact]
    public async Task should_find_tenant_by_normalized_identifier()
    {
        // given
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdentifierAsync(seed.Identifier, AbortToken);

        // then
        found.Should().NotBeNull();
        found!.Id.Should().Be(seed.Id);
        found.Identifier.Should().Be(seed.Identifier);
        found.Name.Should().Be(seed.Name);
        found.IsEnabled.Should().Be(seed.IsEnabled);
    }

    [Fact]
    public async Task should_find_tenant_by_id()
    {
        // given
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdAsync(seed.Id, AbortToken);

        // then
        found.Should().NotBeNull();
        found!.Id.Should().Be(seed.Id);
        found.Identifier.Should().Be(seed.Identifier);
    }

    [Fact]
    public async Task should_return_null_for_unknown_identifier()
    {
        // given
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdentifierAsync(
            $"unknown-{Faker.Random.Hexadecimal(8, prefix: "").ToLowerInvariant()}",
            AbortToken
        );

        // then
        found.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_unknown_id()
    {
        // given
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdAsync($"ten_{Faker.Random.Hexadecimal(8, prefix: "")}", AbortToken);

        // then
        found.Should().BeNull();
    }

    [Fact]
    public async Task should_not_match_identifier_differing_only_by_case()
    {
        // given - stores compare ordinally and never re-normalize (R7); a differently-cased query against
        // an already-normalized stored identifier must miss. Case-insensitive matching (AE7) is the
        // catalog service's job, not the store's.
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdentifierAsync(seed.Identifier.ToUpperInvariant(), AbortToken);

        // then
        found.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_duplicate_normalized_identifiers()
    {
        // given
        var identifier = $"dup-{Faker.Random.Hexadecimal(8, prefix: "").ToLowerInvariant()}";
        var first = TenantSeedFaker.Create(Faker, identifier: identifier);
        var second = TenantSeedFaker.Create(Faker, identifier: identifier);

        // when
        var act = async () => await fixture.SeedAsync([first, second], AbortToken);

        // then - AE10: the concrete exception type is provider-specific (see ITenantCatalogStoreFixture).
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_enumerate_all_tenants_including_disabled()
    {
        // given
        var enabled = TenantSeedFaker.Create(Faker, isEnabled: true);
        var disabled = TenantSeedFaker.Create(Faker, isEnabled: false);
        var store = await fixture.SeedAsync([enabled, disabled], AbortToken);
        var directory = store.Should().BeAssignableTo<ITenantDirectory>().Subject;

        // when
        var all = await directory.GetAllAsync(AbortToken);

        // then
        all.Should().HaveCount(2);
        all.Should().Contain(tenant => tenant.Id == enabled.Id && tenant.IsEnabled);
        all.Should().Contain(tenant => tenant.Id == disabled.Id && !tenant.IsEnabled);
    }

    [Fact]
    public async Task should_surface_disabled_tenant_through_lookup_without_rejecting()
    {
        // given - store-level reads never reject on disablement (R9); rejection is resolution-time only.
        var disabled = TenantSeedFaker.Create(Faker, isEnabled: false);
        var store = await fixture.SeedAsync([disabled], AbortToken);

        // when
        var byIdentifier = await store.FindByIdentifierAsync(disabled.Identifier, AbortToken);
        var byId = await store.FindByIdAsync(disabled.Id, AbortToken);

        // then
        byIdentifier.Should().NotBeNull();
        byIdentifier!.IsEnabled.Should().BeFalse();
        byId.Should().NotBeNull();
        byId!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task should_round_trip_extra_properties()
    {
        // given
        var seed = TenantSeedFaker.Create(Faker) with
        {
            ExtraProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["region"] = "eu-west-1",
                ["plan"] = "enterprise",
            },
        };
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var found = await store.FindByIdentifierAsync(seed.Identifier, AbortToken);

        // then
        found.Should().NotBeNull();
        found!.ExtraProperties.Should().ContainKey("region");
        found.ExtraProperties["region"]!.ToString().Should().Be("eu-west-1");
        found.ExtraProperties.Should().ContainKey("plan");
        found.ExtraProperties["plan"]!.ToString().Should().Be("enterprise");
    }

    [Fact]
    public async Task should_return_isolated_tenant_info_instances_on_repeated_lookups()
    {
        // given - ITenantStore's contract requires a freshly materialized TenantInfo per call: the
        // catalog service hands a store result straight to application code on a cache miss, so an
        // implementation backed by an in-process cache or a seeded dictionary must not alias its own
        // state across calls (see ITenantStore remarks).
        var seed = TenantSeedFaker.Create(Faker);
        var store = await fixture.SeedAsync([seed], AbortToken);

        // when
        var firstByIdentifier = await store.FindByIdentifierAsync(seed.Identifier, AbortToken);
        var secondByIdentifier = await store.FindByIdentifierAsync(seed.Identifier, AbortToken);
        var firstById = await store.FindByIdAsync(seed.Id, AbortToken);
        var secondById = await store.FindByIdAsync(seed.Id, AbortToken);

        // then - repeated lookups return distinct instances, not aliases of the store's own state
        firstByIdentifier.Should().NotBeNull();
        secondByIdentifier.Should().NotBeNull();
        firstById.Should().NotBeNull();
        secondById.Should().NotBeNull();
        secondByIdentifier.Should().NotBeSameAs(firstByIdentifier);
        secondByIdentifier!.ExtraProperties.Should().NotBeSameAs(firstByIdentifier!.ExtraProperties);
        secondById.Should().NotBeSameAs(firstById);
        secondById!.ExtraProperties.Should().NotBeSameAs(firstById!.ExtraProperties);

        // and - mutating a caller-owned instance must not leak into a later lookup
        firstByIdentifier.ExtraProperties["poisoned"] = "true";

        var thirdByIdentifier = await store.FindByIdentifierAsync(seed.Identifier, AbortToken);
        thirdByIdentifier.Should().NotBeNull();
        thirdByIdentifier!.ExtraProperties.Should().NotContainKey("poisoned");
    }
}
