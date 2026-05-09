// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Mediator;

namespace Tests;

public sealed class AllowMissingTenantAttributeTests
{
    [Fact]
    public void should_target_class_and_struct_without_inheritance_or_multiples()
    {
        // when
        var attribute = typeof(AllowMissingTenantAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Single();

        // then
        var usage = attribute.Should().BeOfType<AttributeUsageAttribute>().Subject;
        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct);
        usage.Inherited.Should().BeFalse();
        usage.AllowMultiple.Should().BeFalse();
    }
}
