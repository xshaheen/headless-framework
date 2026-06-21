// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class CrossTenantWriteExceptionTests
{
    [Fact]
    public void constructor_should_set_entity_type_operation_and_message()
    {
        // when
        var ex = new CrossTenantWriteException("Order", "Add");

        // then
        ex.EntityType.Should().Be("Order");
        ex.Operation.Should().Be("Add");
        ex.Message.Should().Contain("Order").And.Contain("Add");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void constructor_with_inner_exception_should_preserve_the_cause()
    {
        // given
        var inner = new InvalidOperationException("boom");

        // when
        var ex = new CrossTenantWriteException("Order", "Modified", inner);

        // then
        ex.InnerException.Should().BeSameAs(inner);
        ex.EntityType.Should().Be("Order");
        ex.Operation.Should().Be("Modified");
        ex.Message.Should().Contain("Order").And.Contain("Modified");
    }
}
