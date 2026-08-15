// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class InMemoryTenantStoreTests : TestBase
{
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
