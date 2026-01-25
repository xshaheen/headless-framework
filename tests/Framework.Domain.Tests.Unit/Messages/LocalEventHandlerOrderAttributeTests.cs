// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Tests.Messages;

public sealed class LocalEventHandlerOrderAttributeTests
{
    [Fact]
    public void should_store_order_value()
    {
        var attribute = new LocalEventHandlerOrderAttribute(42);

        attribute.Order.Should().Be(42);
    }

    [Fact]
    public void should_support_negative_order_values()
    {
        var attribute = new LocalEventHandlerOrderAttribute(-10);

        attribute.Order.Should().Be(-10);
    }

    [Fact]
    public void should_be_applicable_to_class()
    {
        var attributeUsage = typeof(LocalEventHandlerOrderAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        attributeUsage.ValidOn.Should().Be(AttributeTargets.Class);
    }
}
