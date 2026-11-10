// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class SignedUrlAccessLogTests : TestBase
{
    private static readonly BlobLocation _Report = new(SignedUrlTestApp.Container, "tenant-42/q3.pdf");

    [Fact]
    public async Task should_log_one_accepted_entry_with_a_fingerprint_and_no_token_or_location()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken, configureBuilder: logs.Attach);
        var url = await _MintAsync(app);
        var token = url.Segments[^1];
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);

        // then: the fingerprint correlates the request without carrying the credential or the blob's key
        var entry = logs.Entries.Should().ContainSingle().Subject;
        entry.Should().Contain("Download accepted");
        entry.Should().Contain("expires");
        entry.Should().NotContain(token).And.NotContain("tenant-42");
        _Fingerprint(entry).Should().MatchRegex("^[0-9a-f]{12}$");
    }

    [Fact]
    public async Task should_log_the_same_fingerprint_for_the_same_url()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken, configureBuilder: logs.Attach);
        var url = await _MintAsync(app);
        using var client = app.CreateClient();

        // when
        using (await client.GetAsync(url, AbortToken)) { }

        using (await client.GetAsync(url, AbortToken)) { }

        // then
        logs.Entries.Select(_Fingerprint).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public async Task should_log_why_a_token_was_rejected()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken, configureBuilder: logs.Attach);
        var url = await _MintAsync(app);
        using var client = app.CreateClient();

        // when: used for the wrong operation, then not a token at all, then expired
        using (var body = new StringContent("x"))
        using (await client.PutAsync(url, body, AbortToken)) { }

        using (await client.GetAsync(new Uri("/blobs/not-a-token", UriKind.Relative), AbortToken)) { }

        app.Time.Advance(TimeSpan.FromMinutes(16));

        using (await client.GetAsync(url, AbortToken)) { }

        // then
        logs.Entries.Should()
            .SatisfyRespectively(
                first => first.Should().Contain("Upload wrong-operation"),
                second => second.Should().Contain("Download unreadable"),
                third => third.Should().Contain("Download expired")
            );
    }

    [Fact]
    public async Task should_name_the_blob_only_when_the_app_opts_in()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        await using var app = await SignedUrlTestApp.StartAsync(
            AbortToken,
            configureBuilder: builder =>
            {
                logs.Attach(builder);
                builder.Services.Configure<BlobSignedUrlOptions>(options => options.LogBlobLocation = true);
            }
        );
        var url = await _MintAsync(app);
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);

        // then
        logs.Entries.Should().ContainSingle().Which.Should().Contain("blob reports/tenant-42/q3.pdf");
    }

    private static async Task<Uri> _MintAsync(SignedUrlTestApp app)
    {
        await using (var stream = new MemoryStream("report-bytes"u8.ToArray()))
        {
            await app.DefaultStorage.UploadAsync(_Report, stream, cancellationToken: AbortToken);
        }

        return await app
            .DefaultStorage.Should()
            .BeAssignableTo<IPresignedUrlBlobStorage>()
            .Subject.GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), cancellationToken: AbortToken);
    }

    private static string _Fingerprint(string entry)
    {
        const string marker = "token ";
        var start = entry.IndexOf(marker, StringComparison.Ordinal) + marker.Length;

        return entry[start..entry.IndexOf(',', start)];
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public void Attach(WebApplicationBuilder builder) => builder.Logging.AddProvider(this);

        // Only the endpoint's own entries: the hosting request lines name the path too.
        public ILogger CreateLogger(string categoryName) =>
            string.Equals(categoryName, "Headless.Blobs.SignedUrlEndpoint", StringComparison.Ordinal)
                ? new Logger(_entries)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> entries) : ILogger
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
            ) => entries.Enqueue(formatter(state, exception));
        }
    }
}
