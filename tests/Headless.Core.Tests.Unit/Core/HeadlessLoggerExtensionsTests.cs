// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;
using Microsoft.Extensions.Logging;

namespace Tests.Core;

public sealed class HeadlessLoggerExtensionsTests
{
    [Fact]
    public void log_information_should_not_invoke_the_state_builder_when_level_disabled()
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
    public void log_information_should_invoke_the_state_builder_when_level_enabled()
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
}
