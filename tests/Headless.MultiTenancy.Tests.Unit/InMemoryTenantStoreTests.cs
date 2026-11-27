// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class InMemoryTenantStoreTests : TestBase
{
    [Fact]
    public async Task should_snapshot_nested_seed_metadata_and_give_each_reader_its_own_bag()
    {
        var seed = new TenantInfo("ten_1", "Acme", "Acme", isEnabled: true);
        var regions = new List<string> { "eu" };
        seed.ExtraProperties["routing"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["regions"] = regions,
        };

        InMemoryTenantStore store;
        using (var document = JsonDocument.Parse("""{"locale":"en"}"""))
        {
            seed.ExtraProperties["preferences"] = document.RootElement;
            store = new InMemoryTenantStore(Options.Create(new InMemoryTenantStoreOptions { Tenants = [seed] }));
        }

        regions[0] = "mutated";
        var first = await store.FindByIdentifierAsync("acme", AbortToken);
        var routing = first!.ExtraProperties["routing"].Should().BeOfType<JsonElement>().Which;
        routing.GetProperty("regions")[0].GetString().Should().Be("eu");
        first.ExtraProperties["routing"] = "replaced";

        var second = await store.FindByIdAsync("ten_1", AbortToken);
        routing = second!.ExtraProperties["routing"].Should().BeOfType<JsonElement>().Which;
        routing.GetProperty("regions")[0].GetString().Should().Be("eu");
        var preferences = second.ExtraProperties["preferences"].Should().BeOfType<JsonElement>().Which;
        preferences.GetProperty("locale").GetString().Should().Be("en");
    }

    [Fact]
    public void should_reject_seed_metadata_that_cannot_be_serialized_as_json()
    {
        var seed = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        seed.ExtraProperties["unsupported"] = typeof(InMemoryTenantStoreTests);
        var options = Options.Create(new InMemoryTenantStoreOptions { Tenants = [seed] });

        var create = () => new InMemoryTenantStore(options);

        create.Should().Throw<NotSupportedException>();
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
}
