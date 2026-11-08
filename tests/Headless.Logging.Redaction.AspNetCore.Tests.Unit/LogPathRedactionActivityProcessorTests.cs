// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tests;

public sealed class LogPathRedactionActivityProcessorTests : TestBase
{
    private const string _SourceName = "Tests.LogPathRedaction";

    [Theory]
    [InlineData("url.full", "https://cdn.example.com/blobs/abc123?sig=1", "https://cdn.example.com/blobs/***")]
    [InlineData("http.target", "/blobs/abc123?x=1#f", "/blobs/***")]
    [InlineData("url.path", "/blobs/abc123", "/blobs/***")]
    public void should_replace_a_declared_path_in_every_path_tag_and_drop_its_query(
        string tag,
        string value,
        string expected
    )
    {
        // when
        var span = _Record(tag, value, new PrefixRedactor("/blobs"));

        // then
        span.GetTagItem(tag).Should().Be(expected);
    }

    [Theory]
    [InlineData("url.full", "https://cdn.example.com/files/abc123?sig=1")]
    [InlineData("url.path", "/files/abc123")]
    [InlineData("url.full", "not a url")]
    public void should_leave_a_path_no_redactor_declares_unchanged(string tag, string value)
    {
        // when
        var span = _Record(tag, value, new PrefixRedactor("/blobs"));

        // then
        span.GetTagItem(tag).Should().Be(value);
    }

    [Fact]
    public void should_leave_tags_unchanged_when_no_redactor_is_registered()
    {
        // when
        var span = _Record("url.path", "/blobs/abc123", redactor: null);

        // then
        span.GetTagItem("url.path").Should().Be("/blobs/abc123");
    }

    [Fact]
    public void should_register_each_request_hook_once_when_setup_runs_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessRequestLogRedaction();
        services.AddHeadlessRequestLogRedaction();

        // then
        services.Count(d => d.ServiceType == typeof(IStartupFilter)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IHttpLoggingInterceptor)).Should().Be(1);
    }

    private static Activity _Record(string tag, string value, ILogPathRedactor? redactor)
    {
        var spans = new List<Activity>();

        using (
            var provider = Sdk.CreateTracerProviderBuilder()
                .ConfigureServices(services =>
                {
                    if (redactor is not null)
                    {
                        services.AddSingleton(redactor);
                    }
                })
                .AddSource(_SourceName)
                .AddHeadlessLogPathRedaction()
                .AddInMemoryExporter(spans)
                .Build()
        )
        using (var source = new ActivitySource(_SourceName))
        using (var activity = source.StartActivity("request", ActivityKind.Client))
        {
            activity!.SetTag(tag, value);
        }

        return spans.Should().ContainSingle().Subject;
    }

    private sealed class PrefixRedactor(string prefix) : ILogPathRedactor
    {
        public bool TryRedact(string path, [NotNullWhen(true)] out string? redacted)
        {
            redacted = path.StartsWith(prefix + "/", StringComparison.Ordinal) ? prefix + "/***" : null;

            return redacted is not null;
        }
    }
}
