// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class MessagingConventionsExtensionsTests
{
    [Fact]
    public void use_kebab_case_topics_should_mutate_conventions_and_return_same_instance()
    {
        // given
        var conventions = new MessagingConventions();

        // when
        var result = conventions.UseKebabCaseTopics();

        // then
        result.Should().BeSameAs(conventions);
        conventions.TopicNaming.Should().Be(TopicNamingConvention.KebabCase);
    }

    [Fact]
    public void topic_prefix_suffix_and_default_group_should_mutate_only_the_targeted_properties()
    {
        // given
        var conventions = new MessagingConventions
        {
            TopicNaming = TopicNamingConvention.TypeName,
            TopicPrefix = "before.",
            TopicSuffix = ".old",
            DefaultGroup = "before-group",
        };

        // when
        conventions.WithTopicPrefix("after.").WithTopicSuffix(".new").WithDefaultGroup("after-group");

        // then
        conventions.TopicNaming.Should().Be(TopicNamingConvention.TypeName);
        conventions.TopicPrefix.Should().Be("after.");
        conventions.TopicSuffix.Should().Be(".new");
        conventions.DefaultGroup.Should().Be("after-group");
    }

    [Fact]
    public void extension_helpers_should_support_chaining_without_resetting_prior_values()
    {
        // given
        var conventions = new MessagingConventions();

        // when
        var result = conventions
            .UseKebabCaseTopics()
            .WithTopicPrefix("orders.")
            .WithTopicSuffix(".v1")
            .WithDefaultGroup("billing");

        // then
        result.Should().BeSameAs(conventions);
        conventions.TopicNaming.Should().Be(TopicNamingConvention.KebabCase);
        conventions.TopicPrefix.Should().Be("orders.");
        conventions.TopicSuffix.Should().Be(".v1");
        conventions.DefaultGroup.Should().Be("billing");
    }
}
