// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Api.ServiceDefaults.Internal;
using Headless.Context;
using Headless.Logging;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Logging;

namespace Tests.Logging;

public sealed class RequestLogEnricherTests : TestBase
{
    [Fact]
    public void should_add_user_account_correlation_ip_and_headers_inside_a_request()
    {
        // given
        var context = _Context(
            userId: "u-1",
            accountId: "a-1",
            correlationId: "corr-1",
            ip: "203.0.113.7",
            headers: new()
            {
                ["User-Agent"] = "agent/1.0",
                ["X-Client-Version"] = "2.3",
                ["Api-Version"] = "1",
            }
        );

        // when
        var tags = _Enrich(context);

        // then
        tags.Should()
            .BeEquivalentTo(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["UserId"] = "u-1",
                    ["AccountId"] = "a-1",
                    ["CorrelationId"] = "corr-1",
                    ["ClientIp"] = "203.0.113.7",
                    ["UserAgent"] = "agent/1.0",
                    ["XClientVersion"] = "2.3",
                    ["ApiVersion"] = "1",
                }
            );
    }

    [Fact]
    public void should_add_nothing_for_an_anonymous_request_without_ip_or_headers()
    {
        // given
        var context = _Context();

        // when
        var tags = _Enrich(context);

        // then
        tags.Should().BeEmpty();
    }

    [Fact]
    public void should_add_nothing_and_not_throw_outside_a_request()
    {
        // given
        var enricher = new RequestLogEnricher(new HttpContextAccessor(), new HeadlessLoggingOptions());
        var collector = new Collector();

        // when
        var act = () => enricher.Enrich(collector);

        // then
        act.Should().NotThrow();
        collector.Tags.Should().BeEmpty();
    }

    [Fact]
    public void should_truncate_a_long_header_to_the_configured_length()
    {
        // given
        var context = _Context(headers: new() { ["User-Agent"] = new string('a', 2000) });

        // when
        var tags = _Enrich(context, new HeadlessLoggingOptions { MaxHeaderLength = 64 });

        // then
        ((string)tags["UserAgent"])
            .Should()
            .HaveLength(64);
    }

    [Fact]
    public void should_strip_line_breaks_and_escape_sequences_from_a_header()
    {
        // given
        var context = _Context(headers: new() { ["User-Agent"] = "agent\r\nInjected: yes\u001b[31m" });

        // when
        var tags = _Enrich(context);

        // then
        tags["UserAgent"].Should().Be("agentInjected: yes");
    }

    [Fact]
    public void should_read_the_user_on_each_entry_so_authentication_later_in_the_request_shows()
    {
        // given: the first entry is written before authentication sets the user
        var user = Substitute.For<ICurrentUser>();
        var context = _Context(user: user);
        _Enrich(context).Should().NotContainKey("UserId");

        // when
        user.UserId.Returns(new UserId("u-late"));
        var tags = _Enrich(context);

        // then
        tags["UserId"].Should().Be("u-late");
    }

    [Fact]
    public async Task should_carry_request_fields_on_an_entry_written_by_an_endpoint_of_an_add_headless_host()
    {
        // given
        using var sink = new CapturingProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.AddHeadless(options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenApi.Enabled = false;
        });
        builder.Logging.AddProvider(sink);

        await using var app = builder.Build();
        app.MapGet(
            "/log",
            (ILoggerFactory factory) =>
            {
                factory.CreateLogger("Tests.Endpoint").LogWarning("inside the request");
                return "ok";
            }
        );
        await app.StartAsync(AbortToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("probe/9.9");

        // when
        using var response = await client.GetAsync("/log", AbortToken);

        // then
        response.EnsureSuccessStatusCode();
        var (_, properties) = sink.Entries.Should().ContainSingle(e => e.Message == "inside the request").Subject;
        properties.Should().Contain(new KeyValuePair<string, object?>("UserAgent", "probe/9.9"));
        properties.Keys.Should().Contain(["ClientIp", "service.name", "process.pid"]);
    }

    private static Dictionary<string, object> _Enrich(HttpContext context, HeadlessLoggingOptions? options = null)
    {
        var enricher = new RequestLogEnricher(
            new HttpContextAccessor { HttpContext = context },
            options ?? new HeadlessLoggingOptions()
        );
        var collector = new Collector();
        enricher.Enrich(collector);

        return collector.Tags;
    }

    private static DefaultHttpContext _Context(
        string? userId = null,
        string? accountId = null,
        string? correlationId = null,
        string? ip = null,
        Dictionary<string, string>? headers = null,
        ICurrentUser? user = null
    )
    {
        if (user is null)
        {
            user = Substitute.For<ICurrentUser>();
            user.UserId.Returns(userId is null ? null : new UserId(userId));
            user.AccountId.Returns(accountId is null ? null : new AccountId(accountId));
        }

        var webClient = Substitute.For<IWebClientInfoProvider>();
        webClient.IpAddress.Returns(ip);

        var requestContext = Substitute.For<IRequestContext>();
        requestContext.User.Returns(user);
        requestContext.CorrelationId.Returns(correlationId);
        requestContext.WebClient.Returns(webClient);

        var services = new ServiceCollection();
        services.AddSingleton(requestContext);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        foreach (var (name, value) in headers ?? [])
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }

    private sealed class Collector : IEnrichmentTagCollector
    {
        public Dictionary<string, object> Tags { get; } = new(StringComparer.Ordinal);

        public void Add(string tagName, object tagValue) => Tags[tagName] = tagValue;
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly Lock _lock = new();

        public List<(string Message, Dictionary<string, object?> Properties)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (var (key, value) in values)
                    {
                        properties[key] = value;
                    }
                }

                lock (owner._lock)
                {
                    owner.Entries.Add((formatter(state, exception), properties));
                }
            }
        }
    }
}
