// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Nats;
using NATS.Client;
using NATS.Client.JetStream;

namespace Tests;

public sealed class MessagingNatsOptionsTests : TestBase
{
    [Fact]
    public void should_have_default_server_url()
    {
        // given, when
        var options = new MessagingNatsOptions();

        // then
        options.Servers.Should().Be("nats://127.0.0.1:4222");
    }

    [Fact]
    public void should_have_default_connection_pool_size()
    {
        // given, when
        var options = new MessagingNatsOptions();

        // then
        options.ConnectionPoolSize.Should().Be(10);
    }

    [Fact]
    public void should_enable_stream_creation_by_default()
    {
        // given, when
        var options = new MessagingNatsOptions();

        // then
        options.EnableSubscriberClientStreamAndSubjectCreation.Should().BeTrue();
    }

    [Fact]
    public void should_support_multiple_servers()
    {
        // given, when
        var options = new MessagingNatsOptions
        {
            Servers = "nats://server1:4222,nats://server2:4222,nats://server3:4222",
        };

        // then
        options.Servers.Should().Contain("server1");
        options.Servers.Should().Contain("server2");
        options.Servers.Should().Contain("server3");
    }

    [Fact]
    public void should_parse_credentials_from_url()
    {
        // given, when
        var options = new MessagingNatsOptions { Servers = "nats://user:password@localhost:4222" };

        // then
        options.Servers.Should().Contain("user:password");
        options.Servers.Should().Contain("localhost:4222");
    }

    [Fact]
    public void should_have_default_stream_name_normalizer()
    {
        // given
        var options = new MessagingNatsOptions();

        // when
        var normalized = options.NormalizeStreamName("orders.created");

        // then
        normalized.Should().Be("orders");
    }

    [Fact]
    public void should_support_custom_stream_name_normalizer()
    {
        // given
        var options = new MessagingNatsOptions { NormalizeStreamName = origin => origin.ToUpperInvariant() };

        // when
        var normalized = options.NormalizeStreamName("orders.created");

        // then
        normalized.Should().Be("ORDERS.CREATED");
    }

    [Fact]
    public void should_allow_null_nats_options()
    {
        // given, when
        var options = new MessagingNatsOptions { Options = null };

        // then
        options.Options.Should().BeNull();
    }

    [Fact]
    public void should_support_custom_nats_options()
    {
        // given
        var natsOpts = ConnectionFactory.GetDefaultOptions();
        natsOpts.Timeout = 15000;

        // when
        var options = new MessagingNatsOptions { Options = natsOpts };

        // then
        options.Options.Should().NotBeNull();
        options.Options!.Timeout.Should().Be(15000);
    }

    [Fact]
    public void should_allow_null_stream_options()
    {
        // given, when
        var options = new MessagingNatsOptions { StreamOptions = null };

        // then
        options.StreamOptions.Should().BeNull();
    }

    [Fact]
    public void should_support_stream_options_builder()
    {
        // given
        var invoked = false;
        var options = new MessagingNatsOptions
        {
            StreamOptions = builder =>
            {
                invoked = true;
                builder.WithStorageType(StorageType.File);
            },
        };

        // when
        options.StreamOptions?.Invoke(StreamConfiguration.Builder());

        // then
        invoked.Should().BeTrue();
    }

    [Fact]
    public void should_allow_null_consumer_options()
    {
        // given, when
        var options = new MessagingNatsOptions { ConsumerOptions = null };

        // then
        options.ConsumerOptions.Should().BeNull();
    }

    [Fact]
    public void should_support_consumer_options_builder()
    {
        // given
        var invoked = false;
        var options = new MessagingNatsOptions
        {
            ConsumerOptions = builder =>
            {
                invoked = true;
                builder.WithAckWait(60000);
            },
        };

        // when
        options.ConsumerOptions?.Invoke(ConsumerConfiguration.Builder());

        // then
        invoked.Should().BeTrue();
    }

    [Fact]
    public void should_allow_null_custom_headers_builder()
    {
        // given, when
        var options = new MessagingNatsOptions { CustomHeadersBuilder = null };

        // then
        options.CustomHeadersBuilder.Should().BeNull();
    }

    [Fact]
    public void should_disable_stream_creation()
    {
        // given, when
        var options = new MessagingNatsOptions { EnableSubscriberClientStreamAndSubjectCreation = false };

        // then
        options.EnableSubscriberClientStreamAndSubjectCreation.Should().BeFalse();
    }

    [Fact]
    public void should_handle_stream_name_without_dot()
    {
        // given
        var options = new MessagingNatsOptions();

        // when
        var normalized = options.NormalizeStreamName("simplestream");

        // then
        normalized.Should().Be("simplestream");
    }

    [Fact]
    public void should_handle_stream_name_with_multiple_dots()
    {
        // given
        var options = new MessagingNatsOptions();

        // when
        var normalized = options.NormalizeStreamName("orders.us.east.created");

        // then
        normalized.Should().Be("orders");
    }
}
