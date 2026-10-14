// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.Logging;

public sealed class HeadlessLoggerExtensionsTests
{
    [Fact]
    public void should_not_invoke_the_state_builder_when_log_information_level_disabled()
    {
        // given
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Information).Returns(false);
        var builderInvoked = false;

        // when
        logger.LogInformation(
            s =>
            {
                builderInvoked = true;
                return s;
            },
            "message"
        );

        // then
        builderInvoked.Should().BeFalse();
    }

    [Fact]
    public void should_invoke_the_state_builder_when_log_information_level_enabled()
    {
        // given
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Information).Returns(true);
        var builderInvoked = false;

        // when
        logger.LogInformation(
            s =>
            {
                builderInvoked = true;
                return s;
            },
            "message"
        );

        // then
        builderInvoked.Should().BeTrue();
    }

    [Fact]
    public void should_bind_a_message_template_scope_to_the_logging_abstractions_overload()
    {
        // given
        var logger = Substitute.For<ILogger>();
        object? capturedState = null;
        logger.BeginScope(Arg.Do<object>(state => capturedState = state));

        // when
        logger.BeginScope("Order {OrderId}", 42);

        // then
        capturedState.Should().NotBeOfType<LogState>();
        capturedState
            .Should()
            .BeAssignableTo<IReadOnlyList<KeyValuePair<string, object?>>>()
            .Which.Should()
            .Contain(new KeyValuePair<string, object?>("OrderId", 42));
    }

    [Fact]
    public void should_push_a_single_property_scope()
    {
        // given
        var logger = Substitute.For<ILogger>();
        object? capturedState = null;
        logger.BeginScope(Arg.Do<object>(state => capturedState = state));

        // when
        logger.BeginPropertyScope("OrderId", 42);

        // then
        capturedState
            .Should()
            .BeOfType<LogState>()
            .Which.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object?>("OrderId", 42));
    }
}
