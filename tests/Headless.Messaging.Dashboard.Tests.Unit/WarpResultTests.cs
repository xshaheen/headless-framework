// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class WarpResultTests : TestBase
{
    [Fact]
    public void ChildCount_should_return_values_count()
    {
        // given
        var result = new WarpResult
        {
            Group = "test-group",
            Values =
            [
                new WarpResult.SubInfo
                {
                    Topic = "topic1",
                    ImplName = "Impl1",
                    MethodEscaped = "Method1",
                },
                new WarpResult.SubInfo
                {
                    Topic = "topic2",
                    ImplName = "Impl2",
                    MethodEscaped = "Method2",
                },
                new WarpResult.SubInfo
                {
                    Topic = "topic3",
                    ImplName = "Impl3",
                    MethodEscaped = "Method3",
                },
            ],
        };

        // when & then
        result.ChildCount.Should().Be(3);
    }

    [Fact]
    public void ChildCount_should_return_zero_for_empty_values()
    {
        // given
        var result = new WarpResult { Group = "empty-group", Values = [] };

        // when & then
        result.ChildCount.Should().Be(0);
    }

    [Fact]
    public void should_set_and_get_Group()
    {
        // given
        var result = new WarpResult { Group = "test-group", Values = [] };

        // when & then
        result.Group.Should().Be("test-group");
    }

    [Fact]
    public void SubInfo_should_set_and_get_properties()
    {
        // given
        var subInfo = new WarpResult.SubInfo
        {
            Topic = "user.created",
            ImplName = "UserCreatedHandler",
            MethodEscaped = "public async Task HandleAsync(UserCreatedEvent e);",
        };

        // when & then
        subInfo.Topic.Should().Be("user.created");
        subInfo.ImplName.Should().Be("UserCreatedHandler");
        subInfo.MethodEscaped.Should().Contain("HandleAsync");
    }
}
