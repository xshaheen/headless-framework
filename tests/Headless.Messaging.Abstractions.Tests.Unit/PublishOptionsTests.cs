// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PublishOptionsTests : TestBase
{
    [Fact]
    public void should_default_tenant_id_to_null()
    {
        // when
        var options = new PublishOptions();

        // then
        options.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_round_trip_tenant_id_value()
    {
        // given
        const string tenantId = "acme";

        // when
        var options = new PublishOptions { TenantId = tenantId };

        // then
        options.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void should_allow_explicit_null_tenant_id()
    {
        // when
        var options = new PublishOptions { TenantId = null };

        // then
        options.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_store_oversized_tenant_id_without_setter_validation()
    {
        // given
        // PublishOptions.TenantId has no setter validation; length is enforced downstream
        // by MessagePublishRequestFactory at publish time.
        var oversized = new string('x', MessageOptions.TenantIdMaxLength + 1);

        // when
        var options = new PublishOptions { TenantId = oversized };

        // then
        options.TenantId.Should().Be(oversized);
    }

    [Fact]
    public void should_expose_tenant_id_max_length_constant()
    {
        // then
        MessageOptions.TenantIdMaxLength.Should().Be(200);
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

    [Fact]
    public void should_compare_publish_options_by_message_name()
    {
        // given
        var left = new PublishOptions { MessageName = "orders.placed" };
        var matching = new PublishOptions { MessageName = "orders.placed" };
        var different = new PublishOptions { MessageName = "orders.cancelled" };

        // then
        left.Should().Be(matching);
        left.Should().NotBe(different);
    }

    [Fact]
    public void should_compare_enqueue_options_by_message_name()
    {
        // given
        var left = new EnqueueOptions { MessageName = "orders.placed" };
        var matching = new EnqueueOptions { MessageName = "orders.placed" };
        var different = new EnqueueOptions { MessageName = "orders.cancelled" };

        // then
        left.Should().Be(matching);
        left.Should().NotBe(different);
    }
}
