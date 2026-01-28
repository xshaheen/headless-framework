// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Authentication;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarTlsOptionsTests : TestBase
{
    [Fact]
    public void should_have_default_use_tls_from_pulsar_client_configuration()
    {
        // given
        var options = new PulsarTlsOptions();

        // when, then - defaults come from PulsarClientConfiguration.Default
        options.UseTls.Should().BeFalse(); // Default is typically false
    }

    [Fact]
    public void should_allow_setting_use_tls()
    {
        // given
        var options = new PulsarTlsOptions { UseTls = true };

        // when, then
        options.UseTls.Should().BeTrue();
    }

    [Fact]
    public void should_have_tls_hostname_verification_enable_property()
    {
        // given
        var options = new PulsarTlsOptions { TlsHostnameVerificationEnable = true };

        // when, then
        options.TlsHostnameVerificationEnable.Should().BeTrue();
    }

    [Fact]
    public void should_have_tls_allow_insecure_connection_property()
    {
        // given
        var options = new PulsarTlsOptions { TlsAllowInsecureConnection = true };

        // when, then
        options.TlsAllowInsecureConnection.Should().BeTrue();
    }

    [Fact]
    public void should_have_tls_protocols_property()
    {
        // given
        var options = new PulsarTlsOptions { TlsProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 };

        // when, then
        options.TlsProtocols.Should().HaveFlag(SslProtocols.Tls12);
        options.TlsProtocols.Should().HaveFlag(SslProtocols.Tls13);
    }
}
