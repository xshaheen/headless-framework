// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;

namespace Tests.Abstractions;

public sealed class WebClientAuditClientInfoProviderTests : TestBase
{
    [Fact]
    public void should_report_the_web_client_ip_address_and_user_agent()
    {
        // given
        var webClient = Substitute.For<IWebClientInfoProvider>();
        webClient.IpAddress.Returns("203.0.113.7");
        webClient.UserAgent.Returns("agent/1.0");

        // when
        var sut = new WebClientAuditClientInfoProvider(webClient);

        // then
        sut.IpAddress.Should().Be("203.0.113.7");
        sut.UserAgent.Should().Be("agent/1.0");
    }
}
