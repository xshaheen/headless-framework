// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// EF-only scenarios that need direct <see cref="TenantRecord"/>/<see cref="TenantCatalogDbContext"/>
/// access below the provider-neutral <see cref="ITenantCatalogStoreFixture"/> seam: the collation proof
/// (KTD6 — case-only variants collide, accent-surviving values stay distinct) and the identifier-update
/// path (KTD6 — <c>SetIdentifier</c> recomputes the normalized key so only the new identifier resolves,
/// and an update that collides with an existing normalized identifier fails on the unique index).
/// </summary>
/// <typeparam name="TFixture">The leaf fixture that owns this provider's Testcontainers database.</typeparam>
public abstract class TenantCatalogEfSpecificTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, ITenantCatalogEfFixture
{
    [Fact]
    public async Task should_collide_case_only_variants_as_duplicate_tenants()
    {
        // given - KTD6: the unique index is pinned to a case-sensitive collation, but two identifiers that
        // both normalize (trim, lowercase) to the same value still collide at insert time.
        await fixture.ResetAsync(AbortToken);
        await using (var db = new TenantCatalogDbContext(fixture.DbOptions))
        {
            db.Add(new TenantRecord("ten_1", "Acme", "Acme Inc"));
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        var act = async () =>
        {
            await using var db = new TenantCatalogDbContext(fixture.DbOptions);
            db.Add(new TenantRecord("ten_2", "ACME", "Acme Duplicate"));
            await db.SaveChangesAsync(AbortToken);
        };

        // then
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task should_treat_accent_surviving_normalization_as_distinct_tenants()
    {
        // given - normalization only trims and lowercases (no accent folding); an accented identifier and
        // its unaccented counterpart normalize to different values and must both be storable and resolve
        // independently.
        await fixture.ResetAsync(AbortToken);

        await using (var db = new TenantCatalogDbContext(fixture.DbOptions))
        {
            db.Add(new TenantRecord("ten_accented", "café", "Café Tenant"));
            db.Add(new TenantRecord("ten_plain", "cafe", "Cafe Tenant"));
            await db.SaveChangesAsync(AbortToken);
        }

        var store = await fixture.GetStoreAsync(AbortToken);

        // when
        var accented = await store.FindByIdentifierAsync("café", AbortToken);
        var plain = await store.FindByIdentifierAsync("cafe", AbortToken);

        // then
        accented.Should().NotBeNull();
        accented!.Id.Should().Be("ten_accented");
        plain.Should().NotBeNull();
        plain!.Id.Should().Be("ten_plain");
    }

    [Fact]
    public async Task should_recompute_normalized_identifier_and_resolve_only_new_identifier_after_update()
    {
        // given
        await fixture.ResetAsync(AbortToken);
        await using (var db = new TenantCatalogDbContext(fixture.DbOptions))
        {
            db.Add(new TenantRecord("ten_1", "Acme", "Acme Inc"));
            await db.SaveChangesAsync(AbortToken);
        }

        // when - a rebrand: the tenant's public identifier changes after creation
        await using (var db = new TenantCatalogDbContext(fixture.DbOptions))
        {
            var record = await db.Set<TenantRecord>().SingleAsync(x => x.Id == "ten_1", AbortToken);
            record.SetIdentifier("NewAcme");
            await db.SaveChangesAsync(AbortToken);
        }

        var store = await fixture.GetStoreAsync(AbortToken);
        var oldIdentifier = await store.FindByIdentifierAsync("acme", AbortToken);
        var newIdentifier = await store.FindByIdentifierAsync("newacme", AbortToken);

        // then
        oldIdentifier.Should().BeNull();
        newIdentifier.Should().NotBeNull();
        newIdentifier!.Id.Should().Be("ten_1");
        newIdentifier.Identifier.Should().Be("newacme");
    }

    [Fact]
    public async Task should_fail_update_that_collides_with_an_existing_normalized_identifier()
    {
        // given
        await fixture.ResetAsync(AbortToken);
        await using (var db = new TenantCatalogDbContext(fixture.DbOptions))
        {
            db.Add(new TenantRecord("ten_1", "Acme", "Acme Inc"));
            db.Add(new TenantRecord("ten_2", "Globex", "Globex Corp"));
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        var act = async () =>
        {
            await using var db = new TenantCatalogDbContext(fixture.DbOptions);
            var record = await db.Set<TenantRecord>().SingleAsync(x => x.Id == "ten_2", AbortToken);
            record.SetIdentifier("ACME");
            await db.SaveChangesAsync(AbortToken);
        };

        // then
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
