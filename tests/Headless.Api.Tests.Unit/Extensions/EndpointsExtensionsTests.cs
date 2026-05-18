// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tests.Extensions;

public sealed class EndpointsExtensionsTests : TestBase
{
    [Fact]
    public void should_preserve_path_and_query_on_configured_redirect_host()
    {
        // given
        var mainHost = new Uri("https://www.example.com");
        var path = new PathString("/docs/search");
        var query = new QueryString("?q=Headless&returnUrl=https%3A%2F%2Fevil.example");

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result
            .AbsoluteUri.Should()
            .Be("https://www.example.com/docs/search?q=Headless&returnUrl=https%3A%2F%2Fevil.example");
    }

    [Fact]
    public void should_keep_protocol_relative_paths_on_configured_redirect_host()
    {
        // given
        var mainHost = new Uri("https://www.example.com");
        var path = new PathString("//evil.example/login");
        var query = new QueryString("?continue=https%3A%2F%2Fevil.example");

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result.Scheme.Should().Be("https");
        result.Host.Should().Be("www.example.com");
        result
            .AbsoluteUri.Should()
            .Be("https://www.example.com//evil.example/login?continue=https%3A%2F%2Fevil.example");
    }

    [Fact]
    public void should_preserve_configured_non_default_port()
    {
        // given
        var mainHost = new Uri("https://www.example.com:8443");
        var path = new PathString("/health");
        var query = QueryString.Empty;

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result.AbsoluteUri.Should().Be("https://www.example.com:8443/health");
    }
}
