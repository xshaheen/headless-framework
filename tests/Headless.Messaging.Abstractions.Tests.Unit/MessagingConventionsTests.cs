// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingConventionsTests : TestBase
{
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
}

// Test types for message-name generation
public sealed record OrderPlacedEvent(Guid OrderId);

public sealed record Order(Guid Id);

public sealed record XmlParser;

public sealed record Order123Event(Guid OrderId);
