// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class OutboxOptionsTests : TestBase
{
    [Fact]
    public void should_not_expose_delivery_mode_on_outbox_records()
    {
        // when
        var publishProperties = typeof(OutboxPublishOptions).GetProperties().Select(property => property.Name);
        var queueProperties = typeof(OutboxQueueOptions).GetProperties().Select(property => property.Name);

        // then
        publishProperties.Should().NotContain("DeliveryMode").And.NotContain("Enlistment");
        queueProperties.Should().NotContain("DeliveryMode").And.NotContain("Enlistment");
    }

    [Fact]
    public void should_expose_delivery_mode_on_autonomous_records_without_enlistment()
    {
        // when
        var publishProperties = typeof(PublishOptions).GetProperties().Select(property => property.Name).ToArray();
        var queueProperties = typeof(QueueOptions).GetProperties().Select(property => property.Name).ToArray();

        // then
        publishProperties.Should().Contain("DeliveryMode").And.NotContain("Enlistment");
        queueProperties.Should().Contain("DeliveryMode").And.NotContain("Enlistment");
    }

    [Fact]
    public void should_compare_outbox_publish_options_equal_when_headers_match_in_different_dictionaries()
    {
        // given
        var left = new OutboxPublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1", ["beta"] = "2" },
        };
        var right = new OutboxPublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["beta"] = "2", ["alpha"] = "1" },
        };

        // then
        left.Headers.Should().NotBeSameAs(right.Headers);
        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void should_compare_outbox_queue_options_equal_when_headers_match_in_different_dictionaries()
    {
        // given
        var left = new OutboxQueueOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1", ["beta"] = "2" },
        };
        var right = new OutboxQueueOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["beta"] = "2", ["alpha"] = "1" },
        };

        // then
        left.Headers.Should().NotBeSameAs(right.Headers);
        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void should_not_compare_outbox_publish_options_equal_when_a_header_value_differs()
    {
        // given
        var left = new OutboxPublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1" },
        };
        var right = new OutboxPublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "2" },
        };

        // then
        left.Should().NotBe(right);
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }

    [Fact]
    public void should_not_compare_outbox_queue_options_equal_when_a_header_value_differs()
    {
        // given
        var left = new OutboxQueueOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1" },
        };
        var right = new OutboxQueueOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "2" },
        };

        // then
        left.Should().NotBe(right);
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }

    [Fact]
    public void should_preserve_every_other_member_when_a_with_expression_changes_one_on_publish()
    {
        // given
        var original = _FullyPopulatedPublish();

        // when
        var mutated = original with
        {
            MessageId = "changed",
        };

        // then
        mutated.MessageId.Should().Be("changed");
        _AssertOnlyMessageIdChanged(original, mutated);
    }

    [Fact]
    public void should_preserve_every_other_member_when_a_with_expression_changes_one_on_queue()
    {
        // given
        var original = _FullyPopulatedQueue();

        // when
        var mutated = original with
        {
            MessageId = "changed",
        };

        // then
        mutated.MessageId.Should().Be("changed");
        _AssertOnlyMessageIdChanged(original, mutated);
    }

    [Fact]
    public void should_not_compare_outbox_records_equal_across_lanes_or_to_autonomous_records()
    {
        // given
        var publish = new OutboxPublishOptions { MessageName = "orders.placed" };
        var queue = new OutboxQueueOptions { MessageName = "orders.placed" };
        var autonomous = new PublishOptions { MessageName = "orders.placed" };

        // then
        publish.Should().NotBe(queue);
        queue.Should().NotBe(publish);
        publish.Should().NotBe(autonomous);
    }

    private static OutboxPublishOptions _FullyPopulatedPublish() =>
        new()
        {
            Delay = TimeSpan.FromMinutes(3),
            MessageName = "orders.placed",
            ContractVersion = "7",
            RoutingAffinityKey = "customer-42",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "checkout" },
            MessageId = "original",
            CorrelationId = "corr",
            CausationId = "cause",
            SuppressAmbientBusinessContext = true,
            CorrelationSequence = 9,
            CallbackName = "orders.placed.reply",
            TenantId = "acme",
        };

    private static OutboxQueueOptions _FullyPopulatedQueue() =>
        new()
        {
            ScheduledAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            MessageName = "orders.placed",
            ContractVersion = "7",
            RoutingAffinityKey = "customer-42",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "checkout" },
            MessageId = "original",
            CorrelationId = "corr",
            CausationId = "cause",
            SuppressAmbientBusinessContext = true,
            CorrelationSequence = 9,
            CallbackName = "orders.placed.reply",
            TenantId = "acme",
        };

    private static void _AssertOnlyMessageIdChanged(MessageOptions original, MessageOptions mutated)
    {
        var compared = 0;

        foreach (
            var property in mutated
                .GetType()
                .GetProperties()
                .Where(property =>
                    !string.Equals(property.Name, nameof(MessageOptions.MessageId), StringComparison.Ordinal)
                )
        )
        {
            property
                .GetValue(mutated)
                .Should()
                .BeEquivalentTo(property.GetValue(original), $"'{property.Name}' must survive the with expression");

            compared++;
        }

        // Guards the loop itself: an empty property set would make every assertion above vacuous.
        compared.Should().BeGreaterThan(10);
    }
}
