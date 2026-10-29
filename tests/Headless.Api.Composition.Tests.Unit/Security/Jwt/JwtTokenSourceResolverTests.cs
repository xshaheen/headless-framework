// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Security.Jwt;

public sealed class JwtTokenSourceResolverTests : TestBase
{
    private const JwtTokenSources _All = JwtTokenSources.Header | JwtTokenSources.Cookie | JwtTokenSources.QueryString;

    // The cookie source resolves the same-origin options; the defaults admit only the request's own origin.
    private static readonly ServiceProvider _Services = new ServiceCollection().AddOptions().BuildServiceProvider();

    [Fact]
    public void should_prefer_the_header_over_the_cookie_and_the_query_string()
    {
        var context = _Context(
            "/hubs/chat",
            header: "Bearer header-token",
            cookie: "cookie-token",
            query: "query-token"
        );

        JwtTokenSourceResolver.Resolve(context, _Options(_All)).Should().Be("header-token");
    }

    [Fact]
    public void should_prefer_the_cookie_over_the_query_string()
    {
        var context = _Context("/hubs/chat", cookie: "cookie-token", query: "query-token");

        JwtTokenSourceResolver.Resolve(context, _Options(_All)).Should().Be("cookie-token");
    }

    [Fact]
    public void should_fall_back_to_the_query_string_on_a_listed_path()
    {
        var context = _Context("/hubs/chat", query: "query-token");

        JwtTokenSourceResolver.Resolve(context, _Options(_All)).Should().Be("query-token");
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/hubsx")]
    [InlineData("/")]
    public void should_ignore_the_query_string_outside_the_listed_paths(string path)
    {
        var context = _Context(path, query: "query-token");

        JwtTokenSourceResolver.Resolve(context, _Options(JwtTokenSources.QueryString)).Should().BeNull();
    }

    [Fact]
    public void should_ignore_a_cookie_on_a_cross_site_request()
    {
        var context = _Context(
            "/me",
            cookie: "cookie-token",
            origin: "https://evil.example.net",
            fetchSite: "cross-site"
        );

        JwtTokenSourceResolver.Resolve(context, _Options(JwtTokenSources.Cookie)).Should().BeNull();
    }

    [Fact]
    public void should_ignore_a_cookie_without_origin_evidence()
    {
        var context = _Context("/me", cookie: "cookie-token", origin: null, fetchSite: null);

        JwtTokenSourceResolver.Resolve(context, _Options(JwtTokenSources.Cookie)).Should().BeNull();
    }

    [Fact]
    public void should_still_read_the_header_on_a_cross_site_request()
    {
        var context = _Context(
            "/me",
            header: "Bearer header-token",
            origin: "https://evil.example.net",
            fetchSite: "cross-site"
        );

        JwtTokenSourceResolver
            .Resolve(context, _Options(JwtTokenSources.Header | JwtTokenSources.Cookie))
            .Should()
            .Be("header-token");
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public void should_skip_a_header_that_carries_no_bearer_token(string header)
    {
        var context = _Context("/me", header: header, cookie: "cookie-token");

        JwtTokenSourceResolver.Resolve(context, _Options(_All)).Should().Be("cookie-token");
    }

    [Fact]
    public void should_ignore_the_header_when_the_header_source_is_off()
    {
        var context = _Context("/me", header: "Bearer header-token");

        JwtTokenSourceResolver.Resolve(context, _Options(JwtTokenSources.Cookie)).Should().BeNull();
    }

    [Fact]
    public void should_stop_the_handler_reading_the_header_when_the_header_source_is_off()
    {
        var message = _MessageReceived(_Context("/me", header: "Bearer header-token"));

        JwtTokenSourceResolver.Apply(message, _Options(JwtTokenSources.Cookie));

        message.Token.Should().BeNull();
        message.Result.Should().NotBeNull();
        message.Result!.None.Should().BeTrue();
    }

    [Fact]
    public void should_leave_the_handler_to_read_the_header_when_the_header_source_is_on()
    {
        var message = _MessageReceived(_Context("/me"));

        JwtTokenSourceResolver.Apply(message, _Options(_All));

        message.Token.Should().BeNull();
        message.Result.Should().BeNull();
    }

    [Fact]
    public async Task should_run_the_consumer_handler_first_and_keep_its_token()
    {
        var events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = "consumer-token";
                return Task.CompletedTask;
            },
        };
        JwtTokenSourceResolver.Attach(events, _Options(_All));
        var message = _MessageReceived(_Context("/me", cookie: "cookie-token"));

        await events.MessageReceived(message);

        message.Token.Should().Be("consumer-token");
    }

    [Fact]
    public async Task should_apply_the_sources_when_the_consumer_handler_sets_no_token()
    {
        var consumerCalls = 0;
        var events = new JwtBearerEvents
        {
            OnMessageReceived = _ =>
            {
                consumerCalls++;
                return Task.CompletedTask;
            },
        };
        JwtTokenSourceResolver.Attach(events, _Options(_All));
        var message = _MessageReceived(_Context("/me", cookie: "cookie-token"));

        await events.MessageReceived(message);

        consumerCalls.Should().Be(1);
        message.Token.Should().Be("cookie-token");
    }

    [Fact]
    public void should_leave_the_events_untouched_for_the_header_only_default()
    {
        var events = new JwtBearerEvents();
        var original = events.OnMessageReceived;

        JwtTokenSourceResolver.Attach(events, _Options(JwtTokenSources.Header));

        events.OnMessageReceived.Should().BeSameAs(original);
    }

    private static HeadlessJwtBearerOptions _Options(JwtTokenSources sources)
    {
        return new HeadlessJwtBearerOptions
        {
            TokenSources = sources,
            CookieName = "access",
            QueryPathPrefixes = ["/hubs"],
        };
    }

    private static DefaultHttpContext _Context(
        string path,
        string? header = null,
        string? cookie = null,
        string? query = null,
        string? origin = "https://api.example.com",
        string? fetchSite = "same-origin"
    )
    {
        var context = new DefaultHttpContext { RequestServices = _Services };
        var request = context.Request;
        request.Scheme = "https";
        request.Host = new HostString("api.example.com");
        request.Path = path;

        if (header is not null)
        {
            request.Headers.Authorization = header;
        }

        if (cookie is not null)
        {
            request.Headers.Cookie = $"access={cookie}";
        }

        if (query is not null)
        {
            request.QueryString = QueryString.Create("access_token", query);
        }

        if (origin is not null)
        {
            request.Headers.Origin = origin;
        }

        if (fetchSite is not null)
        {
            request.Headers["Sec-Fetch-Site"] = fetchSite;
        }

        return context;
    }

    private static MessageReceivedContext _MessageReceived(HttpContext context)
    {
        return new MessageReceivedContext(
            context,
            new AuthenticationScheme("Bearer", displayName: null, typeof(JwtBearerHandler)),
            new JwtBearerOptions()
        );
    }
}
