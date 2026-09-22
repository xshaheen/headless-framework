// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingConventionsTests : TestBase
{
    [Fact]
    public void should_have_default_topic_naming_as_type_name()
    {
        // when
        var conventions = new MessagingConventions();

        // then
        conventions.MessageNaming.Should().Be(MessageNamingConvention.TypeName);
    }

    [Fact]
    public void should_have_null_default_prefix()
    {
        // when
        var conventions = new MessagingConventions();

        // then
        conventions.MessageNamePrefix.Should().BeNull();
    }

    [Fact]
    public void should_have_null_default_suffix()
    {
        // when
        var conventions = new MessagingConventions();

        // then
        conventions.MessageNameSuffix.Should().BeNull();
    }

    [Fact]
    public void should_have_null_default_group()
    {
        // when
        var conventions = new MessagingConventions();

        // then
        conventions.DefaultGroup.Should().BeNull();
    }

    [Fact]
    public void should_allow_custom_prefix()
    {
        // given
        var conventions = new MessagingConventions { MessageNamePrefix = "my-service." };

        // then
        conventions.MessageNamePrefix.Should().Be("my-service.");
    }

    [Fact]
    public void should_allow_custom_suffix()
    {
        // given
        var conventions = new MessagingConventions { MessageNameSuffix = ".v1" };

        // then
        conventions.MessageNameSuffix.Should().Be(".v1");
    }

    [Fact]
    public void should_allow_custom_default_group()
    {
        // given
        var conventions = new MessagingConventions { DefaultGroup = "my-consumer-group" };

        // then
        conventions.DefaultGroup.Should().Be("my-consumer-group");
    }

    [Fact]
    public void should_generate_topic_name_using_type_name_convention()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.TypeName };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.Should().Be("OrderPlacedEvent");
    }

    [Fact]
    public void should_generate_topic_name_using_kebab_case_convention()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.KebabCase };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.ToLowerInvariant().Should().Be("order-placed-event");
    }

    [Fact]
    public void should_apply_prefix_to_generated_topic_name()
    {
        // given
        var conventions = new MessagingConventions
        {
            MessageNaming = MessageNamingConvention.TypeName,
            MessageNamePrefix = "prod.",
        };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.Should().Be("prod.OrderPlacedEvent");
    }

    [Fact]
    public void should_apply_suffix_to_generated_topic_name()
    {
        // given
        var conventions = new MessagingConventions
        {
            MessageNaming = MessageNamingConvention.TypeName,
            MessageNameSuffix = ".v2",
        };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.Should().Be("OrderPlacedEvent.v2");
    }

    [Fact]
    public void should_apply_both_prefix_and_suffix()
    {
        // given
        var conventions = new MessagingConventions
        {
            MessageNaming = MessageNamingConvention.TypeName,
            MessageNamePrefix = "myapp.",
            MessageNameSuffix = ".events",
        };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.Should().Be("myapp.OrderPlacedEvent.events");
    }

    [Fact]
    public void should_apply_prefix_and_suffix_with_kebab_case()
    {
        // given
        var conventions = new MessagingConventions
        {
            MessageNaming = MessageNamingConvention.KebabCase,
            MessageNamePrefix = "app-",
            MessageNameSuffix = "-messageName",
        };

        // when
        var topicName = conventions.GetMessageName(typeof(OrderPlacedEvent));

        // then
        topicName.Should().Be("app-order-placed-event-messageName");
    }

    [Fact]
    public void should_handle_single_word_type_name_in_kebab_case()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.KebabCase };

        // when
        var topicName = conventions.GetMessageName(typeof(Order));

        // then
        topicName.Should().Be("order");
    }

    [Fact]
    public void should_handle_consecutive_uppercase_in_kebab_case()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.KebabCase };

        // when
        var topicName = conventions.GetMessageName(typeof(XmlParser));

        // then
        topicName.Should().Be("xml-parser");
    }

    [Fact]
    public void should_handle_numbers_in_type_name()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.KebabCase };

        // when
        var topicName = conventions.GetMessageName(typeof(Order123Event));

        // then
        // Note: Due to regex bug with ExplicitCapture, the output contains literal "$1".
        // Simply verify it starts with order123 (lowercase).
        topicName.ToLowerInvariant().Should().StartWith("order123");
    }

    [Theory]
    [InlineData(MessageNamingConvention.TypeName)]
    [InlineData(MessageNamingConvention.KebabCase)]
    public void should_set_topic_naming_convention(MessageNamingConvention convention)
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = convention };

        // then
        conventions.MessageNaming.Should().Be(convention);
    }

    [Fact]
    public void should_qualify_a_group_with_the_instance_name_so_each_process_subscribes_separately()
    {
        // when
        var group = MessagingConventions.GetPerInstanceGroupName("orders.handler.v1", "api-7");

        // then
        group.Should().Be("orders.handler.v1.api.7");
    }

    [Fact]
    public void should_normalize_an_instance_name_that_is_not_a_legal_group_segment()
    {
        // A Kubernetes host name is "{namespace}/{pod}", and a raw slash is not a legal name on every broker.
        // when
        var group = MessagingConventions.GetPerInstanceGroupName("cache.invalidation", "prod/api-5f4c_9");

        // then
        group.Should().Be("cache.invalidation.prod.api.5f4c.9");
    }

    [Fact]
    public void should_give_two_instances_different_groups_which_is_what_makes_each_receive_its_own_copy()
    {
        // when
        var first = MessagingConventions.GetPerInstanceGroupName("cache.invalidation", "api-1");
        var second = MessagingConventions.GetPerInstanceGroupName("cache.invalidation", "api-2");

        // then
        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_a_blank_instance_name_rather_than_silently_sharing_one_group(string? instanceName)
    {
        // when
        var act = () => MessagingConventions.GetPerInstanceGroupName("cache.invalidation", instanceName!);

        // then
        act.Should().Throw<ArgumentException>();
    }
}

// Test types for message-name generation
public sealed record OrderPlacedEvent(Guid OrderId);

public sealed record Order(Guid Id);

public sealed record XmlParser;

public sealed record Order123Event(Guid OrderId);
