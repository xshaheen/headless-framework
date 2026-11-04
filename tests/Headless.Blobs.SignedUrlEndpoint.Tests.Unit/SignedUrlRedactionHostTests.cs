// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tests;

public sealed class SignedUrlRedactionHostTests : TestBase
{
    private const string _HttpLoggingCategory = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";
    private const string _HostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    private static readonly BlobLocation _Report = new(SignedUrlTestApp.Container, "2026/q3.pdf");

    // HEAD is not mapped, so it proves that a request routing answers with 405 is still rewritten.
    [Theory]
    [InlineData("GET", 200)]
    [InlineData("HEAD", 405)]
    public async Task should_log_and_trace_the_path_without_the_token_when_a_signed_url_is_requested(
        string method,
        int expectedStatus
    )
    {
        // given
        using var logs = new CapturingLoggerProvider();
        using var traces = new TraceCapture();
        await using var app = await SignedUrlTestApp.StartAsync(
            AbortToken,
            configureBuilder: builder =>
            {
                builder.Logging.ClearProviders().AddProvider(logs);
                builder.Services.AddHttpLogging(options => options.LoggingFields = HttpLoggingFields.All);
                builder
                    .Services.AddOpenTelemetry()
                    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddProcessor(traces));
            },
            configurePipeline: application => application.UseHttpLogging()
        );
        await using var body = new MemoryStream("report-bytes"u8.ToArray());
        await app.DefaultStorage.UploadAsync(_Report, body, cancellationToken: AbortToken);
        var url = await ((IPresignedUrlBlobStorage)app.DefaultStorage).GetPresignedDownloadUrlAsync(
            _Report,
            TimeSpan.FromMinutes(15),
            cancellationToken: AbortToken
        );
        var token = url.Segments[^1];
        var traceId = ActivityTraceId.CreateRandom();
        var completed = traces.Expect(traceId);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Add(
            "traceparent",
            $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01"
        );

        // when
        using var response = await client.SendAsync(request, AbortToken);
        var activity = await completed.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        ((int)response.StatusCode)
            .Should()
            .Be(expectedStatus);

        activity.GetTagItem("url.path").Should().Be("/blobs/***");
        activity.TagObjects.Select(tag => tag.Value?.ToString()).Should().NotContain(v => v!.Contains(token));

        var httpLogs = logs.Messages(_HttpLoggingCategory);
        httpLogs.Should().Contain(message => message.Contains("Path: /blobs/***", StringComparison.Ordinal));
        httpLogs.Should().NotContain(message => message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_keep_the_token_out_of_the_hosting_request_scope_and_request_lines()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        await using var app = await SignedUrlTestApp.StartAsync(
            AbortToken,
            configureBuilder: builder => builder.Logging.ClearProviders().AddProvider(logs)
        );
        await using var body = new MemoryStream("report-bytes"u8.ToArray());
        await app.DefaultStorage.UploadAsync(_Report, body, cancellationToken: AbortToken);
        var url = await ((IPresignedUrlBlobStorage)app.DefaultStorage).GetPresignedDownloadUrlAsync(
            _Report,
            TimeSpan.FromMinutes(15),
            cancellationToken: AbortToken
        );
        var token = url.Segments[^1];
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);
        await logs.WaitForAsync(_HostingCategory, "Request finished", AbortToken);

        // then - both request lines and the scope every entry of the request carries name the redacted path only
        response.IsSuccessStatusCode.Should().BeTrue();
        var hostingLines = logs.Messages(_HostingCategory);
        hostingLines.Should().Contain(line => line.StartsWith("Request starting", StringComparison.Ordinal));
        hostingLines.Should().Contain(line => line.StartsWith("Request finished", StringComparison.Ordinal));
        hostingLines.Should().OnlyContain(line => line.Contains("/blobs/***", StringComparison.Ordinal));
        logs.Scopes().Should().Contain(scope => scope.Contains("RequestPath:/blobs/***", StringComparison.Ordinal));
        logs.All().Should().NotContain(text => text.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>Hands back the server activity of the request that carried the expected trace id.</summary>
    /// <remarks>
    /// The tracer listens process-wide, so it also hears requests that other tests send to their own hosts; the trace
    /// id picks this test's request out of them.
    /// </remarks>
    private sealed class TraceCapture : BaseProcessor<Activity>
    {
        private readonly ConcurrentDictionary<ActivityTraceId, TaskCompletionSource<Activity>> _expected = new();

        public Task<Activity> Expect(ActivityTraceId traceId)
        {
            var completion = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
            _expected[traceId] = completion;

            return completion.Task;
        }

        public override void OnEnd(Activity data)
        {
            if (data.Kind == ActivityKind.Server && _expected.TryGetValue(data.TraceId, out var completion))
            {
                completion.TrySetResult(data);
            }
        }
    }

    /// <summary>Records every entry's category, rendered message, and the text of each scope it was written under.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<(string Category, string Message, string[] Scopes)> _entries = new();

        private readonly ConcurrentQueue<(string Category, string Prefix, TaskCompletionSource Logged)> _waiters =
            new();

        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public IReadOnlyList<string> Messages(string category)
        {
            return
            [
                .. _entries
                    .Where(e => string.Equals(e.Category, category, StringComparison.Ordinal))
                    .Select(e => e.Message),
            ];
        }

        public IReadOnlyList<string> Scopes() => [.. _entries.SelectMany(e => e.Scopes)];

        public IReadOnlyList<string> All() => [.. _entries.SelectMany(e => e.Scopes.Prepend(e.Message))];

        /// <summary>Completes once an entry of <paramref name="category"/> starting with <paramref name="prefix"/> is logged.</summary>
        /// <remarks>The hosting layer writes "Request finished" after the response is sent, so the client can see the response first.</remarks>
        public Task WaitForAsync(string category, string prefix, CancellationToken cancellationToken)
        {
            var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue((category, prefix, logged));

            if (_Matches(category, prefix))
            {
                logged.TrySetResult();
            }

            return logged.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        private bool _Matches(string category, string prefix) =>
            Messages(category).Any(m => m.StartsWith(prefix, StringComparison.Ordinal));

        private void _Record(string category, string message, string[] scopes)
        {
            _entries.Enqueue((category, message, scopes));

            foreach (var (waitCategory, prefix, logged) in _waiters)
            {
                if (
                    string.Equals(waitCategory, category, StringComparison.Ordinal)
                    && message.StartsWith(prefix, StringComparison.Ordinal)
                )
                {
                    logged.TrySetResult();
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, this);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopes = scopeProvider;
        }

        public void Dispose() { }

        private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return owner._scopes.Push(state);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var scopes = new List<string>();
                owner._scopes.ForEachScope(static (scope, list) => list.Add(scope?.ToString() ?? ""), scopes);
                owner._Record(category, formatter(state, exception), [.. scopes]);
            }
        }
    }
}
