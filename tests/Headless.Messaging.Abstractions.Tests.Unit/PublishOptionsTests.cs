// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PublishOptionsTests : TestBase
{
    [Fact]
    public void should_default_tenantId_to_null()
    {
        // when
        var options = new PublishOptions();

        // then
        options.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_tenantId_value()
    {
        // given
        const string tenantId = "acme";

        // when
        var options = new PublishOptions { TenantId = tenantId };

        // then
        options.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void should_allow_explicit_null_tenantId()
    {
        // when
        var options = new PublishOptions { TenantId = null };

        // then
        options.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_store_oversized_tenantId_without_setter_validation()
    {
        // given
        // PublishOptions.TenantId has no setter validation; length is enforced downstream
        // by MessagePublishRequestFactory at publish time.
        var oversized = new string('x', PublishOptions.TenantIdMaxLength + 1);

        // when
        var options = new PublishOptions { TenantId = oversized };

        // then
        options.TenantId.Should().Be(oversized);
    }

    [Fact]
    public void should_expose_tenantId_max_length_constant()
    {
        // then
        PublishOptions.TenantIdMaxLength.Should().Be(200);
    }

    [Fact]
    public void should_compare_headers_with_ordinal_keys_independent_of_dictionary_comparer()
    {
        // given
        var left = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tenant"] = "acme" },
        };
        var right = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["tenant"] = "acme" },
        };

        // then
        left.Should().NotBe(right);
        right.Should().NotBe(left);
    }

    [Fact]
    public void should_hash_equal_when_headers_have_same_pairs_in_different_order()
    {
        // given
        var left = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1", ["beta"] = "2" },
        };
        var right = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["beta"] = "2", ["alpha"] = "1" },
        };

        // then
        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }
}
