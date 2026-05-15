// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_pulsar_with_server_url()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UsePulsar("pulsar://localhost:6650");

        // then - verify it returns the same setup instance for chaining
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_register_pulsar_with_configure_action()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UsePulsar(opt =>
        {
            opt.ServiceUrl = "pulsar://localhost:6650";
            opt.EnableClientLog = true;
        });

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UsePulsar((Action<MessagingPulsarOptions>)null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_allow_fluent_configuration()
    {
        // given
        var setup = _CreateSetup();

        // when - verify fluent chaining works
        var result = setup.UsePulsar("pulsar://localhost:6650");

        // then
        result.Should().BeSameAs(setup);
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());
    }
}
