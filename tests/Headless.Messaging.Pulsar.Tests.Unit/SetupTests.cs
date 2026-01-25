// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_pulsar_with_server_url()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UsePulsar("pulsar://localhost:6650");

        // then - verify it returns the same options instance for chaining
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_register_pulsar_with_configure_action()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UsePulsar(opt =>
        {
            opt.ServiceUrl = "pulsar://localhost:6650";
            opt.EnableClientLog = true;
        });

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var options = new MessagingOptions();

        // when
        var act = () => options.UsePulsar((Action<MessagingPulsarOptions>)null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_allow_fluent_configuration()
    {
        // given
        var options = new MessagingOptions();

        // when - verify fluent chaining works
        var result = options
            .UsePulsar("pulsar://localhost:6650");

        // then
        result.Should().BeSameAs(options);
    }
}
