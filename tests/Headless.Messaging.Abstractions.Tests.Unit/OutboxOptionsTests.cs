// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class OutboxOptionsTests : TestBase
{
    [Fact]
    public void should_not_expose_delivery_mode_or_enlistment_on_the_outbox_record()
    {
        // when
        var properties = typeof(OutboxOptions).GetProperties().Select(property => property.Name);

        // then
        properties.Should().NotContain("DeliveryMode").And.NotContain("Enlistment");
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
    public void should_compare_equal_when_headers_match_in_different_dictionaries()
    {
        // given
        var left = new OutboxOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1", ["beta"] = "2" },
        };
        var right = new OutboxOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["beta"] = "2", ["alpha"] = "1" },
        };

        // then
        left.Headers.Should().NotBeSameAs(right.Headers);
        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void should_not_compare_equal_when_a_header_value_differs()
    {
        // given
        var left = new OutboxOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "1" },
        };
        var right = new OutboxOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = "2" },
        };

        // then
        left.Should().NotBe(right);
        left.GetHashCode().Should().NotBe(right.GetHashCode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_preserve_every_other_member_when_a_with_expression_changes_one(bool scheduled)
    {
        // given — one record serves both lanes and both scheduling spellings
        var original = _FullyPopulated(scheduled);

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
    public void should_not_compare_equal_to_an_autonomous_record_with_the_same_members()
    {
        // given
        var outbox = new OutboxOptions { MessageName = "orders.placed" };
        var autonomous = new PublishOptions { MessageName = "orders.placed" };

        // then
        outbox.Should().NotBe(autonomous);
    }

    private static OutboxOptions _FullyPopulated(bool scheduled) =>
        new()
        {
            Delay = scheduled ? null : TimeSpan.FromMinutes(3),
            ScheduledAt = scheduled ? new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero) : null,
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
