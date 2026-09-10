// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantInfoSerializationTests
{
    [Fact]
    public void should_round_trip_core_fields_and_extra_properties_through_json_serializer()
    {
        // given
        var tenant = new TenantInfo(id: "ten_123", identifier: "acme", name: "Acme Inc.", isEnabled: true)
        {
            ExtraProperties =
            {
                ["region"] = "eu-west-1",
                ["seats"] = 42,
                ["trial"] = false,
            },
        };

        // when
        var json = JsonSerializer.Serialize(tenant);
        var result = JsonSerializer.Deserialize<TenantInfo>(json);

        // then
        result.Should().NotBeNull();
        result!.Id.Should().Be(tenant.Id);
        result.Identifier.Should().Be(tenant.Identifier);
        result.Name.Should().Be(tenant.Name);
        result.IsEnabled.Should().Be(tenant.IsEnabled);

        result.ExtraProperties.Should().HaveCount(3);
        ((JsonElement)result.ExtraProperties["region"]!).GetString().Should().Be("eu-west-1");
        ((JsonElement)result.ExtraProperties["seats"]!).GetInt32().Should().Be(42);
        ((JsonElement)result.ExtraProperties["trial"]!).GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void should_round_trip_a_null_display_name_and_an_empty_extra_properties_bag()
    {
        // given
        var tenant = new TenantInfo(id: "ten_456", identifier: "beta", name: null, isEnabled: false);

        // when
        var json = JsonSerializer.Serialize(tenant);
        var result = JsonSerializer.Deserialize<TenantInfo>(json);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().BeNull();
        result.IsEnabled.Should().BeFalse();
        result.ExtraProperties.Should().BeEmpty();
    }
}
