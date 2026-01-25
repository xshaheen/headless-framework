// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Hosting.Seeders;

namespace Tests.Seeders;

public sealed class SeederPriorityAttributeTests
{
    [Fact]
    public void should_store_priority_value()
    {
        // given
        const int priority = 42;

        // when
        var attribute = new SeederPriorityAttribute(priority);

        // then
        attribute.Priority.Should().Be(42);
    }

    [Fact]
    public void should_default_to_zero()
    {
        // when
        var attribute = new SeederPriorityAttribute(0);

        // then
        attribute.Priority.Should().Be(0);
    }

    [Fact]
    public void should_be_applicable_to_class()
    {
        // given
        var attributeType = typeof(SeederPriorityAttribute);

        // when
        var usageAttribute = attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .FirstOrDefault();

        // then
        usageAttribute.Should().NotBeNull();
        usageAttribute!.ValidOn.Should().Be(AttributeTargets.Class);
    }
}
