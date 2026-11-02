// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarMessagingOptionsTests : TestBase
{
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

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void should_reject_negative_ack_redelivery_delay_below_client_minimum(int milliseconds)
    {
        var options = new PulsarMessagingOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            NegativeAckRedeliveryDelay = TimeSpan.FromMilliseconds(milliseconds),
        };

        var result = new PulsarMessagingOptionsValidator().Validate(options);

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle(error => error.PropertyName == nameof(PulsarMessagingOptions.NegativeAckRedeliveryDelay));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(60_000)]
    public void should_accept_negative_ack_redelivery_delay_at_or_above_client_minimum(int milliseconds)
    {
        var options = new PulsarMessagingOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            NegativeAckRedeliveryDelay = TimeSpan.FromMilliseconds(milliseconds),
        };

        var result = new PulsarMessagingOptionsValidator().Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_default_producer_settings()
    {
        var producer = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }.Producer;

        producer.CompressionType.Should().Be(DotPulsar.CompressionType.None);
        producer.SendTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void should_accept_custom_producer_settings()
    {
        var options = new PulsarMessagingOptions
        {
            ServiceUrl = "pulsar+ssl://broker:6651",
            Producer = { CompressionType = DotPulsar.CompressionType.Lz4, SendTimeout = TimeSpan.Zero },
        };

        new PulsarMessagingOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_negative_send_timeout()
    {
        var options = new PulsarMessagingOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            Producer = { SendTimeout = TimeSpan.FromMilliseconds(-1) },
        };

        new PulsarMessagingOptionsValidator().Validate(options).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("localhost:6650")]
    [InlineData("http://localhost:6650")]
    [InlineData("pulsar://")]
    public void should_reject_a_service_url_that_is_not_an_absolute_pulsar_url(string serviceUrl)
    {
        var options = new PulsarMessagingOptions { ServiceUrl = serviceUrl };

        var result = new PulsarMessagingOptionsValidator().Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(PulsarMessagingOptions.ServiceUrl));
    }

    [Fact]
    public void should_default_tls_settings_to_verify_the_certificate_authority_only()
    {
        var tls = new PulsarTlsOptions();

        tls.VerifyCertificateAuthority.Should().BeTrue();
        tls.VerifyCertificateName.Should().BeFalse();
        tls.CheckCertificateRevocation.Should().BeTrue();
        tls.TrustedCertificateAuthority.Should().BeNull();
        tls.ClientCertificate.Should().BeNull();
    }

    [Fact]
    public void should_reject_missing_producer_settings()
    {
        var options = new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650", Producer = null! };

        new PulsarMessagingOptionsValidator().Validate(options).IsValid.Should().BeFalse();
    }
}
