// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarMessagingOptionsTests : TestBase
{
    [Fact]
    public void should_require_service_url()
    {
        // given
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.ServiceUrl.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public void should_have_enable_client_log_property()
    {
        // given
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650", EnableClientLog = true };

        // when, then
        options.EnableClientLog.Should().BeTrue();
    }

    [Fact]
    public void should_default_enable_client_log_to_false()
    {
        // given
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.EnableClientLog.Should().BeFalse();
    }

    [Fact]
    public void should_default_negative_ack_redelivery_delay_to_client_default()
    {
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" };

        options.NegativeAckRedeliveryDelay.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void should_support_tls_options()
    {
        // given
        var tlsOptions = new PulsarTlsOptions { TlsHostnameVerificationEnable = true };
        var options = new PulsarMessagingOptions
        {
            ServiceUrl = "pulsar+ssl://localhost:6651",
            TlsOptions = tlsOptions,
        };

        // when, then
        options.TlsOptions.Should().BeSameAs(tlsOptions);
        options.TlsOptions.TlsHostnameVerificationEnable.Should().BeTrue();
    }

    [Fact]
    public void should_have_null_tls_options_by_default()
    {
        // given
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.TlsOptions.Should().BeNull();
    }
}
