// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Blobs;
using Headless.Hosting;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddLogging();
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

    [Fact]
    public async Task should_decorate_the_logger_factory_once_when_setup_runs_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then: one factory registration, wrapped once, so a hosting line is rewritten and not double-wrapped
        services.Count(d => d.ServiceType == typeof(ILoggerFactory)).Should().Be(1);
        provider.GetRequiredService<ILoggerFactory>().GetType().Name.Should().Be("RedactingLoggerFactory");
    }

    [Fact]
    public async Task should_warn_at_startup_when_a_logger_factory_registered_later_replaces_the_redacting_one()
    {
        // given - an app that registers its own logger factory after the endpoint, as UseSerilog does
        var captured = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
        });
        services.AddSingleton<ILoggerFactory>(_ => new LoggerFactory([new EventNameProvider(captured)]));
        await using var provider = services.BuildServiceProvider();

        // when
        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }

        // then
        captured.Should().Contain("LogRedactionFactoryReplaced");
    }

    [Fact]
    public async Task should_redact_again_when_log_redaction_is_added_after_the_later_logger_factory()
    {
        // given - the documented fix: call AddHeadlessRedaction again after the factory that replaced the wrapper
        var captured = new List<(string Message, string? Path, string? ScopePath)>();
        using var stateProvider = new StateProvider(captured);
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
        });
        services.AddSingleton<ILoggerFactory>(_ => new LoggerFactory([stateProvider, new EventNameProvider(events)]));
        services.AddHeadlessRedaction();
        await using var provider = services.BuildServiceProvider();

        // when
        provider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(_HostingCategory)
            .Log(
                LogLevel.Information,
                default,
                new HostingState(("Path", "/blobs/" + _Token)),
                null,
                (st, _) => "line"
            );

        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }

        // then
        captured.Should().ContainSingle().Which.Path.Should().Be("/blobs/***");
        events.Should().NotContain("LogRedactionFactoryReplaced");
    }

    [Theory]
    [InlineData("/blobs/" + _Token, "/blobs/***")]
    [InlineData("/BLOBS/" + _Token, "/BLOBS/***")]
    public void should_rewrite_the_path_in_the_hosting_scope_and_request_lines(string path, string expectedPath)
    {
        // given
        var captured = new List<(string Message, string? Path, string? ScopePath)>();
        using var stateProvider = new StateProvider(captured);
        using var provider = _BuildProvider("https://api.example.com", "/blobs", stateProvider);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(_HostingCategory);

        // when
        using (logger.BeginScope(new HostingState(("RequestPath", path), ("RequestId", "0HN"))))
        {
            logger.Log(
                LogLevel.Information,
                default,
                new HostingState(("PathBase", ""), ("Path", path), ("{OriginalFormat}", "Request starting {Path}")),
                null,
                (state, _) => "Request starting " + state.Get("Path")
            );
        }

        // then
        captured
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(("Request starting " + expectedPath, expectedPath, expectedPath));
    }

    [Fact]
    public void should_leave_other_categories_and_other_paths_untouched()
    {
        // given
        var captured = new List<(string Message, string? Path, string? ScopePath)>();
        using var stateProvider = new StateProvider(captured);
        using var provider = _BuildProvider("https://api.example.com", "/blobs", stateProvider);
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var hosting = factory.CreateLogger(_HostingCategory);
        var other = factory.CreateLogger("App.Orders");

        // when
        hosting.Log(LogLevel.Information, default, new HostingState(("Path", "/orders/7")), null, (s, _) => "line");
        other.Log(LogLevel.Information, default, new HostingState(("Path", "/blobs/" + _Token)), null, (s, _) => "x");

        // then
        captured.Should().Equal(("line", "/orders/7", null), ("x", "/blobs/" + _Token, null));
    }

    private const string _HostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    private static ServiceProvider _BuildProvider(
        string baseUrl,
        string routePrefix,
        ILoggerProvider? loggerProvider = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
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

    /// <summary>A structured log state shaped like ASP.NET Core's hosting scope and request lines.</summary>
    private sealed class HostingState(params (string Key, object? Value)[] values)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => values.Length;

        public KeyValuePair<string, object?> this[int index] => new(values[index].Key, values[index].Value);

        public object? Get(string key) => values.First(v => string.Equals(v.Key, key, StringComparison.Ordinal)).Value;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < values.Length; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Records each entry's message, its state's Path value, and the RequestPath of the scope around it.</summary>
    private sealed class StateProvider(List<(string Message, string? Path, string? ScopePath)> captured)
        : ILoggerProvider,
            ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) => new StateLogger(this, captured);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private static string? _Find(object? state, string key)
        {
            return state is IReadOnlyList<KeyValuePair<string, object?>> values
                ? values.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal)).Value as string
                : null;
        }

        private sealed class StateLogger(
            StateProvider owner,
            List<(string Message, string? Path, string? ScopePath)> captured
        ) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var scopePath = new List<string>();
                owner._scopes.ForEachScope(
                    static (scope, found) =>
                    {
                        if (_Find(scope, "RequestPath") is { } path)
                        {
                            found.Add(path);
                        }
                    },
                    scopePath
                );

                lock (captured)
                {
                    captured.Add((formatter(state, exception), _Find(state, "Path"), scopePath.FirstOrDefault()));
                }
            }
        }
    }

    /// <summary>Records only event names.</summary>
    private sealed class EventNameProvider(List<string> names) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new EventNameLogger(names);

        public void Dispose() { }

        private sealed class EventNameLogger(List<string> names) : ILogger
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
                if (eventId.Name is { } name)
                {
                    lock (names)
                    {
                        names.Add(name);
                    }
                }
            }
        }
    }
}
