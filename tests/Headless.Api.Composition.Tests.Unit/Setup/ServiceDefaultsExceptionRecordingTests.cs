// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.ServiceDefaults;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Tests.Setup;

public sealed class ServiceDefaultsExceptionRecordingTests : TestBase
{
    private const string _NationalId = "29001011234567";

    [Fact]
    public async Task should_record_the_request_spans_exception_from_the_masked_copy()
    {
        // given
        await using var app = _Build(recordException: true);
        var instrumentation = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
        Exception thrown;

        try
        {
            throw new InvalidOperationException("lookup failed", new FormatException($"no citizen {_NationalId}"));
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, nameof(ServiceDefaultsExceptionRecordingTests), StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource(nameof(ServiceDefaultsExceptionRecordingTests));
        using var activity = source.StartActivity("request")!;

        // when: the instrumentation hands the raw exception to its enrich callback
        instrumentation.EnrichWithException!(activity, thrown);

        // then: the instrumentation's own unmasked recording is off, and the event keeps the type and frames
        instrumentation.RecordException.Should().BeFalse();
        var tags = activity.Events.Should().ContainSingle().Which.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags["exception.type"].Should().Be(typeof(InvalidOperationException).FullName);
        tags["exception.stacktrace"]
            .Should()
            .BeOfType<string>()
            .Which.Should()
            .NotContain(_NationalId)
            .And.Contain("no citizen ***")
            .And.Contain(nameof(should_record_the_request_spans_exception_from_the_masked_copy));
    }

    [Fact]
    public async Task should_record_no_exception_event_when_recording_is_off()
    {
        // given
        await using var app = _Build(recordException: false);

        // when
        var instrumentation = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;

        // then
        instrumentation.RecordException.Should().BeFalse();
        instrumentation.EnrichWithException.Should().BeNull();
    }

    private static WebApplication _Build(bool recordException)
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenApi.Enabled = false;
            options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = false;
            options.OpenTelemetry.RecordException = recordException;
        });
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        builder.Services.AddHeadlessRedaction(static setup => setup.MaskText(@"\b\d{14}\b"));

        return builder.Build();
    }
}
