// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class MessagingConventionsExtensionsTests
{
    [Fact]
    public void should_mutate_conventions_and_return_same_instance_when_use_kebab_case_topics()
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
    public void should_mutate_conventions_and_return_same_instance_when_use_type_name_topics()
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
    public void should_mutate_only_the_targeted_properties_when_topic_prefix_suffix_and_default_group()
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
    public void should_support_chaining_without_resetting_prior_values_when_extension_helpers()
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
