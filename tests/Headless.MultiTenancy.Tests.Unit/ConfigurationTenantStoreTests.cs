// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ConfigurationTenantStoreTests : TestBase
{
    [Fact]
    public void should_throw_when_two_seeds_normalize_to_the_same_identifier()
    {
        // given — AE10 configuration arm: "Acme" and " acme " both normalize to "acme"
        var options = Options.Create(
            new ConfigurationTenantStoreOptions
            {
                Tenants =
                [
                    new ConfigurationTenantSeed
                    {
                        Id = "ten_1",
                        Identifier = "Acme",
                        Name = "Acme",
                    },
                    new ConfigurationTenantSeed
                    {
                        Id = "ten_2",
                        Identifier = " acme ",
                        Name = "Acme Duplicate",
                    },
                ],
            }
        );

        // when
        var act = () => new ConfigurationTenantStore(options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate tenant identifier*");
    }

    [Fact]
    public void should_throw_when_two_seeds_share_the_same_tenant_id()
    {
        // given
        var options = Options.Create(
            new ConfigurationTenantStoreOptions
            {
                Tenants =
                [
                    new ConfigurationTenantSeed
                    {
                        Id = "ten_1",
                        Identifier = "acme",
                        Name = "Acme",
                    },
                    new ConfigurationTenantSeed
                    {
                        Id = "ten_1",
                        Identifier = "globex",
                        Name = "Globex",
                    },
                ],
            }
        );

        // when
        var act = () => new ConfigurationTenantStore(options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate tenant id*");
    }

    [Fact]
    public async Task should_convert_seed_extra_properties_into_the_extra_properties_bag()
    {
        // given
        var store = new ConfigurationTenantStore(
            Options.Create(
                new ConfigurationTenantStoreOptions
                {
                    Tenants =
                    [
                        new ConfigurationTenantSeed
                        {
                            Id = "ten_1",
                            Identifier = "acme",
                            Name = "Acme",
                            ExtraProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["Region"] = "eu-west-1",
                                ["Plan"] = "enterprise",
                            },
                        },
                    ],
                }
            )
        );

        // when
        var result = await store.FindByIdAsync("ten_1", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.ExtraProperties["Region"].Should().Be("eu-west-1");
        result.ExtraProperties["Plan"].Should().Be("enterprise");
    }
}
