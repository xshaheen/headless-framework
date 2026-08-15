// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class InMemoryTenantStoreTests : TestBase
{
    [Fact]
    public async Task should_find_tenant_by_normalized_identifier()
    {
        // given
        var store = new InMemoryTenantStore(
            Options.Create(
                new InMemoryTenantStoreOptions
                {
                    Tenants = [new TenantInfo("ten_1", "Acme", "Acme Inc", isEnabled: true)],
                }
            )
        );

        // when
        var result = await store.FindByIdentifierAsync("acme", AbortToken);

        // then — the seed's mixed-case identifier is normalized at construction (R7)
        result.Should().NotBeNull();
        result!.Id.Should().Be("ten_1");
        result.Identifier.Should().Be("acme");
    }

    [Fact]
    public async Task should_return_null_for_unknown_identifier()
    {
        // given
        var store = new InMemoryTenantStore(Options.Create(new InMemoryTenantStoreOptions()));

        // when
        var result = await store.FindByIdentifierAsync("ghost", AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_find_tenant_by_id()
    {
        // given
        var store = new InMemoryTenantStore(
            Options.Create(
                new InMemoryTenantStoreOptions { Tenants = [new TenantInfo("ten_1", "acme", "Acme", isEnabled: true)] }
            )
        );

        // when
        var result = await store.FindByIdAsync("ten_1", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_throw_when_two_seeds_normalize_to_the_same_identifier()
    {
        // given — AE10 in-memory arm: "Acme" and " acme " both normalize to "acme"
        var options = Options.Create(
            new InMemoryTenantStoreOptions
            {
                Tenants =
                [
                    new TenantInfo("ten_1", "Acme", "Acme", isEnabled: true),
                    new TenantInfo("ten_2", " acme ", "Acme Duplicate", isEnabled: true),
                ],
            }
        );

        // when
        var act = () => new InMemoryTenantStore(options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate tenant identifier*");
    }

    [Fact]
    public void should_throw_when_two_seeds_share_the_same_tenant_id()
    {
        // given
        var options = Options.Create(
            new InMemoryTenantStoreOptions
            {
                Tenants =
                [
                    new TenantInfo("ten_1", "acme", "Acme", isEnabled: true),
                    new TenantInfo("ten_1", "globex", "Globex", isEnabled: true),
                ],
            }
        );

        // when
        var act = () => new InMemoryTenantStore(options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate tenant id*");
    }

    [Fact]
    public async Task should_enumerate_all_seeded_tenants_including_disabled()
    {
        // given
        var store = new InMemoryTenantStore(
            Options.Create(
                new InMemoryTenantStoreOptions
                {
                    Tenants =
                    [
                        new TenantInfo("ten_1", "acme", "Acme", isEnabled: true),
                        new TenantInfo("ten_2", "globex", "Globex", isEnabled: false),
                    ],
                }
            )
        );

        // when
        var all = await store.GetAllAsync(AbortToken);

        // then
        all.Should().HaveCount(2);
        all.Should().Contain(tenant => tenant.Id == "ten_1");
        all.Should().Contain(tenant => tenant.Id == "ten_2" && !tenant.IsEnabled);
    }
}
