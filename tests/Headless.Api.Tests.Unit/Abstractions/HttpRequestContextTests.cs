// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Abstractions;

public sealed class HttpRequestContextTests : TestBase
{
    [Fact]
    public void should_return_user_from_current_user()
    {
        // given
        var currentUser = Substitute.For<ICurrentUser>();
        var sut = _CreateSut(currentUser: currentUser);

        // when
        var result = sut.User;

        // then
        result.Should().BeSameAs(currentUser);
    }

    [Fact]
    public void should_return_tenant_from_current_tenant()
    {
        // given
        var currentTenant = Substitute.For<ICurrentTenant>();
        var sut = _CreateSut(currentTenant: currentTenant);

        // when
        var result = sut.Tenant;

        // then
        result.Should().BeSameAs(currentTenant);
    }

    [Fact]
    public void should_return_locale_from_current_locale()
    {
        // given
        var currentLocale = Substitute.For<ICurrentLocale>();
        var sut = _CreateSut(currentLocale: currentLocale);

        // when
        var result = sut.Locale;

        // then
        result.Should().BeSameAs(currentLocale);
    }

    [Fact]
    public void should_return_time_zone_from_current_time_zone()
    {
        // given
        var currentTimeZone = Substitute.For<ICurrentTimeZone>();
        var sut = _CreateSut(currentTimeZone: currentTimeZone);

        // when
        var result = sut.TimeZone;

        // then
        result.Should().BeSameAs(currentTimeZone);
    }

    [Fact]
    public void should_return_web_client_from_provider()
    {
        // given
        var webClientInfoProvider = Substitute.For<IWebClientInfoProvider>();
        var sut = _CreateSut(webClientInfoProvider: webClientInfoProvider);

        // when
        var result = sut.WebClient;

        // then
        result.Should().BeSameAs(webClientInfoProvider);
    }

    [Fact]
    public void should_return_date_started_from_clock()
    {
        // given
        var fixedTime = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var clock = new TestClock(timeProvider);
        var sut = _CreateSut(clock: clock);

        // when
        var result = sut.DateStarted;

        // then
        result.Should().Be(fixedTime);
    }

    [Fact]
    public void should_return_trace_identifier_from_http_context()
    {
        // given
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-abc-123" };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.TraceIdentifier;

        // then
        result.Should().Be("trace-abc-123");
    }

    [Fact]
    public void should_return_correlation_id_from_http_context_header()
    {
        // given
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append(HttpHeaderNames.CorrelationId, "corr-xyz-789");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.CorrelationId;

        // then
        result.Should().Be("corr-xyz-789");
    }

    [Fact]
    public void should_return_null_correlation_id_when_header_missing()
    {
        // given
        var httpContext = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.CorrelationId;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_endpoint_name_from_http_context()
    {
        // given
        var httpContext = new DefaultHttpContext();
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "GET /api/users"
        );
        httpContext.SetEndpoint(endpoint);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.EndpointName;

        // then
        result.Should().Be("GET /api/users");
    }

    [Fact]
    public void should_return_null_endpoint_name_when_no_endpoint()
    {
        // given
        var httpContext = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.EndpointName;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_is_available_true_when_http_context_exists()
    {
        // given
        var httpContext = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.IsAvailable;

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_is_available_false_when_http_context_null()
    {
        // given
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = _CreateSut(accessor: accessor);

        // when
        var result = sut.IsAvailable;

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_accessing_trace_identifier_without_http_context()
    {
        // given
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = _CreateSut(accessor: accessor);

        // when
        var act = () => sut.TraceIdentifier;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*User context is not available*");
    }

    #region Helpers

    private static HttpRequestContext _CreateSut(
        IHttpContextAccessor? accessor = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null,
        ICurrentLocale? currentLocale = null,
        ICurrentTimeZone? currentTimeZone = null,
        IWebClientInfoProvider? webClientInfoProvider = null,
        IClock? clock = null
    )
    {
        if (accessor is null)
        {
            accessor = Substitute.For<IHttpContextAccessor>();
            accessor.HttpContext.Returns(new DefaultHttpContext());
        }

        currentUser ??= Substitute.For<ICurrentUser>();
        currentTenant ??= Substitute.For<ICurrentTenant>();
        currentLocale ??= Substitute.For<ICurrentLocale>();
        currentTimeZone ??= Substitute.For<ICurrentTimeZone>();
        webClientInfoProvider ??= Substitute.For<IWebClientInfoProvider>();
        clock ??= new TestClock(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return new HttpRequestContext(
            accessor,
            currentUser,
            currentTenant,
            currentLocale,
            currentTimeZone,
            webClientInfoProvider,
            clock
        );
    }

    #endregion
}
