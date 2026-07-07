// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using NATS.Client.JetStream.Models;

namespace Tests;

public sealed class NatsMessagingOptionsTests : TestBase
{
    [Fact]
    public void should_have_default_server_url()
    {
        var options = new NatsMessagingOptions();
        options.Servers.Should().Be("nats://127.0.0.1:4222");
    }

    [Fact]
    public void should_have_default_connection_pool_size()
    {
        var options = new NatsMessagingOptions();
        options.ConnectionPoolSize.Should().Be(10);
    }

    [Fact]
    public void should_enable_stream_creation_by_default()
    {
        var options = new NatsMessagingOptions();
        options.EnableSubscriberClientStreamAndSubjectCreation.Should().BeTrue();
    }

    [Fact]
    public void should_support_multiple_servers()
    {
        var options = new NatsMessagingOptions
        {
            Servers = "nats://server1:4222,nats://server2:4222,nats://server3:4222",
        };

        options.Servers.Should().Contain("server1");
        options.Servers.Should().Contain("server2");
        options.Servers.Should().Contain("server3");
    }

    [Fact]
    public void should_redact_credentials_from_single_server_display_value()
    {
        var options = new NatsMessagingOptions { Servers = "nats://user:password@localhost:4222" };

        BrokerAddressDisplay.FormatMany(options.Servers).Should().Be("nats://localhost:4222");
    }

    [Fact]
    public void should_redact_credentials_from_multiple_server_display_value()
    {
        var options = new NatsMessagingOptions
        {
            Servers = "nats://user:password@localhost:4222, nats://admin:secret@example.com:4223",
        };

        BrokerAddressDisplay.FormatMany(options.Servers).Should().Be("nats://localhost:4222,nats://example.com:4223");
    }

    [Fact]
    public void should_have_default_stream_name_normalizer()
    {
        var options = new NatsMessagingOptions();
        options.NormalizeStreamName("orders.created").Should().Be("orders");
    }

    [Fact]
    public void should_support_custom_stream_name_normalizer()
    {
        var options = new NatsMessagingOptions { NormalizeStreamName = origin => origin.ToUpperInvariant() };

        options.NormalizeStreamName("orders.created").Should().Be("ORDERS.CREATED");
    }

    [Fact]
    public void should_handle_stream_name_without_dot()
    {
        var options = new NatsMessagingOptions();
        options.NormalizeStreamName("simplestream").Should().Be("simplestream");
    }

    [Fact]
    public void should_handle_stream_name_with_multiple_dots()
    {
        var options = new NatsMessagingOptions();
        options.NormalizeStreamName("orders.us.east.created").Should().Be("orders");
    }

    [Fact]
    public void should_disable_stream_creation()
    {
        var options = new NatsMessagingOptions { EnableSubscriberClientStreamAndSubjectCreation = false };

        options.EnableSubscriberClientStreamAndSubjectCreation.Should().BeFalse();
    }

    [Fact]
    public void should_build_default_nats_opts()
    {
        var options = new NatsMessagingOptions { Servers = "nats://custom:4222" };
        var natsOpts = options.BuildNatsOpts();

        natsOpts.Url.Should().Be("nats://custom:4222");
    }

    [Fact]
    public void should_apply_configure_connection_callback()
    {
        var options = new NatsMessagingOptions
        {
            Servers = "nats://localhost:4222",
            ConfigureConnection = opts => opts with { ConnectTimeout = TimeSpan.FromSeconds(30) },
        };

        var natsOpts = options.BuildNatsOpts();

        natsOpts.Url.Should().Be("nats://localhost:4222");
        natsOpts.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void should_allow_null_configure_connection()
    {
        var options = new NatsMessagingOptions { ConfigureConnection = null };
        var natsOpts = options.BuildNatsOpts();

        natsOpts.Should().NotBeNull();
    }

    [Fact]
    public void should_support_stream_options_callback()
    {
        var invoked = false;
        var options = new NatsMessagingOptions
        {
            StreamOptions = config =>
            {
                invoked = true;
                config.Storage = StreamConfigStorage.File;
            },
        };

        var config = new StreamConfig();
        options.StreamOptions?.Invoke(config);

        invoked.Should().BeTrue();
        config.Storage.Should().Be(StreamConfigStorage.File);
    }

    [Fact]
    public void should_support_consumer_options_callback()
    {
        var invoked = false;
        var options = new NatsMessagingOptions
        {
            ConsumerOptions = config =>
            {
                invoked = true;
                config.AckWait = TimeSpan.FromMinutes(1);
            },
        };

        var config = new ConsumerConfig("test");
        options.ConsumerOptions?.Invoke(config);

        invoked.Should().BeTrue();
        config.AckWait.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void should_allow_null_stream_options()
    {
        var options = new NatsMessagingOptions { StreamOptions = null };
        options.StreamOptions.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_consumer_options()
    {
        var options = new NatsMessagingOptions { ConsumerOptions = null };
        options.ConsumerOptions.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_custom_headers_builder()
    {
        var options = new NatsMessagingOptions { CustomHeadersBuilder = null };
        options.CustomHeadersBuilder.Should().BeNull();
    }

    // Validator tests

    [Fact]
    public void validator_should_pass_for_valid_options()
    {
        var options = new NatsMessagingOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 5 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void validator_should_fail_for_empty_servers()
    {
        var options = new NatsMessagingOptions { Servers = "" };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.Servers));
    }

    [Fact]
    public void validator_should_fail_for_zero_pool_size()
    {
        var options = new NatsMessagingOptions { ConnectionPoolSize = 0 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.ConnectionPoolSize));
    }

    [Fact]
    public void validator_should_fail_for_negative_pool_size()
    {
        var options = new NatsMessagingOptions { ConnectionPoolSize = -1 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
    }
}
