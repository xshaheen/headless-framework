// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using NATS.Client.Core;
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
    public void should_default_connection_pool_size_to_one()
    {
        var options = new NatsMessagingOptions();
        options.ConnectionPoolSize.Should().Be(1);
    }

    [Fact]
    public void should_default_max_consecutive_consume_failures_to_ten()
    {
        var options = new NatsMessagingOptions();
        options.MaxConsecutiveConsumeFailures.Should().Be(10);
    }

    [Fact]
    public void should_default_stream_provisioning_to_verify()
    {
        var options = new NatsMessagingOptions();
        options.StreamProvisioning.Should().Be(NatsStreamProvisioning.Verify);
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

    // Validator tests

    [Fact]
    public void should_pass_for_valid_options_when_validator()
    {
        var options = new NatsMessagingOptions { Servers = "nats://localhost:4222", ConnectionPoolSize = 5 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_for_empty_servers_when_validator()
    {
        var options = new NatsMessagingOptions { Servers = "" };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.Servers));
    }

    [Fact]
    public void should_fail_for_zero_pool_size_when_validator()
    {
        var options = new NatsMessagingOptions { ConnectionPoolSize = 0 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.ConnectionPoolSize));
    }

    [Fact]
    public void should_fail_for_pool_size_above_one_when_validator_and_use_connection()
    {
        // given
        var options = new NatsMessagingOptions { ConnectionPoolSize = 2 }.UseConnection(_ =>
            Substitute.For<INatsConnection>()
        );
        var validator = new NatsMessagingOptionsValidator();

        // when
        var result = validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.ConnectionPoolSize));
    }

    [Fact]
    public void should_pass_for_default_pool_size_when_validator_and_use_connection()
    {
        // given
        var options = new NatsMessagingOptions().UseConnection(_ => Substitute.For<INatsConnection>());
        var validator = new NatsMessagingOptionsValidator();

        // when
        var result = validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_for_negative_pool_size_when_validator()
    {
        var options = new NatsMessagingOptions { ConnectionPoolSize = -1 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_for_zero_max_consecutive_consume_failures_when_validator()
    {
        var options = new NatsMessagingOptions { MaxConsecutiveConsumeFailures = 0 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.MaxConsecutiveConsumeFailures));
    }

    [Fact]
    public void should_fail_for_undefined_stream_provisioning_when_validator()
    {
        var options = new NatsMessagingOptions { StreamProvisioning = (NatsStreamProvisioning)99 };
        var validator = new NatsMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(NatsMessagingOptions.StreamProvisioning));
    }
}
