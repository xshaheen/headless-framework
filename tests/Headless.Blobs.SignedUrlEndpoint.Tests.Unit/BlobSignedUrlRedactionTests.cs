// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class BlobSignedUrlRedactionTests : TestBase
{
    private const string _Token = "CfDJ8live-token_value";

    [Theory]
    [InlineData("https://api.example.com", "/blobs", "", "/blobs/" + _Token, "/blobs/***")]
    [InlineData("https://api.example.com", "/blobs/", "", "/BLOBS/" + _Token, "/BLOBS/***")]
    [InlineData("https://api.example.com", "/blobs", "", "/blobs/" + _Token + "/extra", "/blobs/***")]
    [InlineData("https://example.com/app/", "/files", "", "/app/files/" + _Token, "/app/files/***")]
    [InlineData("https://example.com/app/", "/files", "/app", "/files/" + _Token, "/files/***")]
    public async Task should_log_the_path_with_the_token_replaced_when_http_logging_sees_a_signed_url(
        string baseUrl,
        string routePrefix,
        string pathBase,
        string path,
        string expectedPath
    )
    {
        // given
        await using var provider = _BuildProvider(baseUrl, routePrefix);
        var logContext = _LogContext(pathBase, path, HttpLoggingFields.All);

        // when
        await _Interceptor(provider).OnRequestAsync(logContext);

        // then
        logContext.IsAnyEnabled(HttpLoggingFields.RequestPath).Should().BeFalse();
        logContext
            .Parameters.Should()
            .Equal(
                new KeyValuePair<string, object?>("PathBase", new PathString(pathBase)),
                new KeyValuePair<string, object?>("Path", new PathString(expectedPath))
            );
        logContext.Parameters.Select(p => p.Value?.ToString()).Should().NotContain(v => v!.Contains(_Token));
    }

    [Theory]
    [InlineData("/blobs")]
    [InlineData("/blobs/")]
    [InlineData("/blobsx/" + _Token)]
    [InlineData("/api/blobs/" + _Token)]
    [InlineData("/app/blobs/" + _Token)]
    public async Task should_leave_http_logging_alone_when_path_is_not_a_signed_url(string path)
    {
        // given
        await using var provider = _BuildProvider("https://api.example.com", "/blobs");
        var logContext = _LogContext(pathBase: "", path, HttpLoggingFields.All);

        // when
        await _Interceptor(provider).OnRequestAsync(logContext);

        // then
        logContext.LoggingFields.Should().Be(HttpLoggingFields.All);
        logContext.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task should_add_no_path_when_http_logging_does_not_log_the_path()
    {
        // given
        await using var provider = _BuildProvider("https://api.example.com", "/blobs");
        var logContext = _LogContext(pathBase: "", "/blobs/" + _Token, HttpLoggingFields.RequestMethod);

        // when
        await _Interceptor(provider).OnRequestAsync(logContext);
        await _Interceptor(provider).OnResponseAsync(logContext);

        // then
        logContext.LoggingFields.Should().Be(HttpLoggingFields.RequestMethod);
        logContext.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task should_replace_the_token_in_every_path_tag_when_the_request_activity_carries_a_signed_url()
    {
        // given
        await using var provider = _BuildProvider("https://api.example.com", "/blobs");
        using var serverActivity = new Activity("Microsoft.AspNetCore.Hosting.HttpRequestIn");
        serverActivity.SetTag("url.path", "/blobs/" + _Token);
        serverActivity.SetTag("http.target", "/blobs/" + _Token + "?download=1");
        serverActivity.SetTag("url.full", "https://api.example.com/blobs/" + _Token);
        serverActivity.SetTag("http.request.method", "GET");
        var context = _HttpContext(pathBase: "", "/blobs/" + _Token, serverActivity);

        // when
        await _RunPipelineAsync(provider, context);

        // then
        serverActivity.GetTagItem("url.path").Should().Be("/blobs/***");
        serverActivity.GetTagItem("http.target").Should().Be("/blobs/***?download=1");
        serverActivity.GetTagItem("url.full").Should().Be("https://api.example.com/blobs/***");
        serverActivity.GetTagItem("http.request.method").Should().Be("GET");
    }

    [Fact]
    public async Task should_replace_the_token_on_the_current_activity_when_instrumentation_started_a_sibling()
    {
        // given
        await using var provider = _BuildProvider("https://example.com/app", "/blobs");
        using var serverActivity = new Activity("Microsoft.AspNetCore.Hosting.HttpRequestIn");
        serverActivity.SetTag("url.path", "/app/blobs/" + _Token);
        using var sibling = new Activity("Microsoft.AspNetCore.Hosting.HttpRequestIn");
        sibling.Start();
        sibling.SetTag("url.path", "/app/blobs/" + _Token);
        var context = _HttpContext(pathBase: "/app", "/blobs/" + _Token, serverActivity);

        // when
        await _RunPipelineAsync(provider, context);

        // then
        serverActivity.GetTagItem("url.path").Should().Be("/app/blobs/***");
        sibling.GetTagItem("url.path").Should().Be("/app/blobs/***");
    }

    [Fact]
    public async Task should_keep_activity_tags_and_call_the_next_middleware_when_path_is_not_a_signed_url()
    {
        // given
        await using var provider = _BuildProvider("https://api.example.com", "/blobs");
        using var serverActivity = new Activity("Microsoft.AspNetCore.Hosting.HttpRequestIn");
        serverActivity.SetTag("url.path", "/api/blobs/" + _Token);
        var context = _HttpContext(pathBase: "", "/api/blobs/" + _Token, serverActivity);

        // when
        var reachedNext = await _RunPipelineAsync(provider, context);

        // then
        reachedNext.Should().BeTrue();
        serverActivity.GetTagItem("url.path").Should().Be("/api/blobs/" + _Token);
    }

    [Fact]
    public async Task should_skip_tags_when_the_request_has_no_activity()
    {
        // given
        await using var provider = _BuildProvider("https://api.example.com", "/blobs");
        var context = _HttpContext(pathBase: "", "/blobs/" + _Token, activity: null);

        // when
        var reachedNext = await _RunPipelineAsync(provider, context);

        // then
        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task should_register_each_redaction_hook_once_when_setup_runs_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<IHttpLoggingInterceptor>().Should().ContainSingle();
        provider.GetServices<IStartupFilter>().Should().ContainSingle();
    }

    private static ServiceProvider _BuildProvider(string baseUrl, string routePrefix)
    {
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options =>
            {
                options.BaseUrl = new Uri(baseUrl);
                options.RoutePrefix = routePrefix;
            });
        });

        return services.BuildServiceProvider();
    }

    private static IHttpLoggingInterceptor _Interceptor(IServiceProvider provider)
    {
        return provider.GetServices<IHttpLoggingInterceptor>().Should().ContainSingle().Subject;
    }

    private static HttpLoggingInterceptorContext _LogContext(string pathBase, string path, HttpLoggingFields fields)
    {
        return new HttpLoggingInterceptorContext
        {
            HttpContext = _HttpContext(pathBase, path, activity: null),
            LoggingFields = fields,
        };
    }

    private static DefaultHttpContext _HttpContext(string pathBase, string path, Activity? activity)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.PathBase = new PathString(pathBase);
        context.Request.Path = new PathString(path);

        if (activity is not null)
        {
            context.Features.Set<IHttpActivityFeature>(new ActivityFeature(activity));
        }

        return context;
    }

    /// <summary>Builds the pipeline the registered startup filter produces and sends one request through it.</summary>
    private static async Task<bool> _RunPipelineAsync(IServiceProvider provider, HttpContext context)
    {
        var filter = provider.GetServices<IStartupFilter>().Should().ContainSingle().Subject;
        var reachedNext = false;
        var app = new ApplicationBuilder(provider);

        filter.Configure(builder =>
            builder.Run(_ =>
            {
                reachedNext = true;

                return Task.CompletedTask;
            })
        )(app);

        await app.Build()(context);

        return reachedNext;
    }

    private sealed class ActivityFeature(Activity activity) : IHttpActivityFeature
    {
        public Activity Activity { get; set; } = activity;
    }
}
