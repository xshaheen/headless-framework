// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class WarpResultTests : TestBase
{
    [Fact]
    public void should_return_values_count_when_child_count()
    {
        // given
        var result = new WarpResult
        {
            Group = "test-group",
            Values =
            [
                new WarpResult.SubInfo
                {
                    MessageName = "topic1",
                    ImplName = "Impl1",
                    MethodEscaped = "Method1",
                },
                new WarpResult.SubInfo
                {
                    MessageName = "topic2",
                    ImplName = "Impl2",
                    MethodEscaped = "Method2",
                },
                new WarpResult.SubInfo
                {
                    MessageName = "topic3",
                    ImplName = "Impl3",
                    MethodEscaped = "Method3",
                },
            ],
        };

        // when & then
        result.ChildCount.Should().Be(3);
    }

    [Fact]
    public void should_return_zero_for_empty_values_when_child_count()
    {
        // given
        var result = new WarpResult { Group = "empty-group", Values = [] };

        // when & then
        result.ChildCount.Should().Be(0);
    }

    [Fact]
    public void should_set_and_get_group()
    {
        // given
        var result = new WarpResult { Group = "test-group", Values = [] };

        // when & then
        result.Group.Should().Be("test-group");
    }

    [Fact]
    public void should_set_and_get_properties_when_sub_info()
    {
        // given
        var subInfo = new WarpResult.SubInfo
        {
            MessageName = "user.created",
            ImplName = "UserCreatedHandler",
            MethodEscaped = "public async Task HandleAsync(UserCreatedEvent e);",
        };

        // when & then
        subInfo.MessageName.Should().Be("user.created");
        subInfo.ImplName.Should().Be("UserCreatedHandler");
        subInfo.MethodEscaped.Should().Contain("HandleAsync");
    }
}
