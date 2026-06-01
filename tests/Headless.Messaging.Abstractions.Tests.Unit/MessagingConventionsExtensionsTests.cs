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
        var result = conventions.UseKebabCaseMessageNames();

        // then
        result.Should().BeSameAs(conventions);
        conventions.MessageNaming.Should().Be(MessageNamingConvention.KebabCase);
    }

    [Fact]
    public void use_type_name_topics_should_mutate_conventions_and_return_same_instance()
    {
        // given
        var conventions = new MessagingConventions { MessageNaming = MessageNamingConvention.KebabCase };

        // when
        var result = conventions.UseTypeNameMessageNames();

        // then
        result.Should().BeSameAs(conventions);
        conventions.MessageNaming.Should().Be(MessageNamingConvention.TypeName);
    }

    [Fact]
    public void topic_prefix_suffix_and_default_group_should_mutate_only_the_targeted_properties()
    {
        // given
        var conventions = new MessagingConventions
        {
            MessageNaming = MessageNamingConvention.TypeName,
            MessageNamePrefix = "before.",
            MessageNameSuffix = ".old",
            DefaultGroup = "before-group",
        };

        // when
        conventions.WithMessageNamePrefix("after.").WithMessageNameSuffix(".new").WithDefaultGroup("after-group");

        // then
        conventions.MessageNaming.Should().Be(MessageNamingConvention.TypeName);
        conventions.MessageNamePrefix.Should().Be("after.");
        conventions.MessageNameSuffix.Should().Be(".new");
        conventions.DefaultGroup.Should().Be("after-group");
    }

    [Fact]
    public void extension_helpers_should_support_chaining_without_resetting_prior_values()
    {
        // given
        var conventions = new MessagingConventions();

        // when
        var result = conventions
            .UseKebabCaseMessageNames()
            .WithMessageNamePrefix("orders.")
            .WithMessageNameSuffix(".v1")
            .WithDefaultGroup("billing");

        // then
        result.Should().BeSameAs(conventions);
        conventions.MessageNaming.Should().Be(MessageNamingConvention.KebabCase);
        conventions.MessageNamePrefix.Should().Be("orders.");
        conventions.MessageNameSuffix.Should().Be(".v1");
        conventions.DefaultGroup.Should().Be("billing");
    }
}
