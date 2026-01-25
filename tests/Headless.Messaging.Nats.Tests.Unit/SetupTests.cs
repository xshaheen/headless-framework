// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.Nats;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_allow_method_chaining_with_bootstrap_servers()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UseNats("nats://custom-server:4222");

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_allow_method_chaining_with_configure_action()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UseNats(opt =>
        {
            opt.Servers = "nats://configured-server:4222";
            opt.ConnectionPoolSize = 20;
        });

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var messagingOptions = new MessagingOptions();

        // when
        var act = () => messagingOptions.UseNats((Action<MessagingNatsOptions>)null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_allow_default_servers_when_bootstrap_servers_is_null()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UseNats(bootstrapServers: null);

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_allow_empty_configure_action()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UseNats(_ => { });

        // then
        result.Should().BeSameAs(options);
    }
}
