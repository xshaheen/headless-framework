// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Abstractions;

public sealed class HttpAbsoluteUrlFactoryTests : TestBase
{
    #region Origin Property

    [Fact]
    public void should_return_origin_from_request()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.Origin;

        // then
        result.Should().Be("https://example.com");
    }

    [Fact]
    public void should_return_origin_with_port()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com", 8443);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.Origin;

        // then
        result.Should().Be("https://example.com:8443");
    }

    [Fact]
    public void should_throw_when_getting_origin_without_http_context()
    {
        // given
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var act = () => sut.Origin;

        // then
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*request is not currently available*");
    }

    [Fact]
    public void should_set_origin_scheme_and_host()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("old.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        sut.Origin = "https://new.example.com";

        // then
        httpContext.Request.Scheme.Should().Be("https");
        httpContext.Request.Host.Value.Should().Be("new.example.com");
    }

    [Fact]
    public void should_set_origin_with_port()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("old.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        sut.Origin = "https://new.example.com:9443";

        // then
        httpContext.Request.Scheme.Should().Be("https");
        httpContext.Request.Host.Value.Should().Be("new.example.com:9443");
    }

    [Fact]
    public void should_throw_when_setting_origin_without_http_context()
    {
        // given
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var act = () => sut.Origin = "https://example.com";

        // then
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*request is not currently available*");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("nocolon")]
    [InlineData("")]
    public void should_throw_when_setting_invalid_origin(string invalidOrigin)
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("old.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var act = () => sut.Origin = invalidOrigin;

        // then
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must contain a scheme and host*");
    }

    #endregion

    #region GetAbsoluteUrl (no HttpContext param)

    [Fact]
    public void should_create_absolute_url_from_relative_path()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl("/users/123");

        // then
        result.Should().Be("https://api.example.com/users/123");
    }

    [Fact]
    public void should_preserve_query_string()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl("/search?q=test&page=2");

        // then
        result.Should().Be("https://api.example.com/search?q=test&page=2");
    }

    [Fact]
    public void should_include_path_base()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        httpContext.Request.PathBase = "/v1";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl("/users/123");

        // then
        result.Should().Be("https://api.example.com/v1/users/123");
    }

    [Fact]
    public void should_return_absolute_url_unchanged()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl("https://other.com/path");

        // then
        result.Should().Be("https://other.com/path");
    }

    [Fact]
    public void should_return_null_when_path_null()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(null!);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_path_malformed()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl("://malformed");

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_getting_absolute_url_without_http_context()
    {
        // given
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var act = () => sut.GetAbsoluteUrl("/users");

        // then
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*request is not currently available*");
    }

    #endregion

    #region GetAbsoluteUrl (with HttpContext param)

    [Fact]
    public void should_create_absolute_url_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, "/orders/456");

        // then
        result.Should().Be("https://api.example.com/orders/456");
    }

    [Fact]
    public void should_preserve_query_string_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, "/orders?status=pending&sort=date");

        // then
        result.Should().Be("https://api.example.com/orders?status=pending&sort=date");
    }

    [Fact]
    public void should_include_path_base_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        httpContext.Request.PathBase = "/api/v2";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, "/products");

        // then
        result.Should().Be("https://api.example.com/api/v2/products");
    }

    [Fact]
    public void should_return_absolute_url_unchanged_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, "https://cdn.example.com/assets/logo.png");

        // then
        result.Should().Be("https://cdn.example.com/assets/logo.png");
    }

    [Fact]
    public void should_return_null_when_path_null_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, null!);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_path_malformed_with_explicit_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpAbsoluteUrlFactory(accessor);

        // when
        var result = sut.GetAbsoluteUrl(httpContext, "://invalid-uri");

        // then
        result.Should().BeNull();
    }

    #endregion
}
