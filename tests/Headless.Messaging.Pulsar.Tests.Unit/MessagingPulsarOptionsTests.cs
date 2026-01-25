// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Pulsar;

namespace Tests;

public sealed class MessagingPulsarOptionsTests : TestBase
{
    [Fact]
    public void should_require_service_url()
    {
        // given
        var options = new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.ServiceUrl.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public void should_have_enable_client_log_property()
    {
        // given
        var options = new MessagingPulsarOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            EnableClientLog = true,
        };

        // when, then
        options.EnableClientLog.Should().BeTrue();
    }

    [Fact]
    public void should_default_enable_client_log_to_false()
    {
        // given
        var options = new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.EnableClientLog.Should().BeFalse();
    }

    [Fact]
    public void should_support_tls_options()
    {
        // given
        var tlsOptions = new PulsarTlsOptions { UseTls = true };
        var options = new MessagingPulsarOptions
        {
            ServiceUrl = "pulsar+ssl://localhost:6651",
            TlsOptions = tlsOptions,
        };

        // when, then
        options.TlsOptions.Should().BeSameAs(tlsOptions);
        options.TlsOptions.UseTls.Should().BeTrue();
    }

    [Fact]
    public void should_have_null_tls_options_by_default()
    {
        // given
        var options = new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" };

        // when, then
        options.TlsOptions.Should().BeNull();
    }
}
