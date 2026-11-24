// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tests;

public sealed class TextRedactionActivityProcessorTests : TestBase
{
    private const string _SourceName = "Tests.TextRedaction";
    private const string _NationalId = "29001011234567";

    [Fact]
    public void should_mask_the_status_description_and_exception_tags_and_keep_the_rest()
    {
        // when: a failure recorded the way the jobs scheduler records it, as plain tags and a status description
        var span = _Record(
            activity =>
            {
                activity.SetTag("exception.type", "InvalidOperationException");
                activity.SetTag("exception.message", $"no citizen {_NationalId}");
                activity.SetTag(
                    "exception.stacktrace",
                    $"InvalidOperationException: no citizen {_NationalId}\n   at Run()"
                );
                activity.SetTag("headless.job.function", "Refunds");
                activity.SetStatus(ActivityStatusCode.Error, $"no citizen {_NationalId}");
            },
            maskText: true
        );

        // then: only the sensitive shape changes; the type, the frames, and the other tags stay for debugging
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("no citizen ***");
        span.GetTagItem("exception.message").Should().Be("no citizen ***");
        span.GetTagItem("exception.stacktrace").Should().Be("InvalidOperationException: no citizen ***\n   at Run()");
        span.GetTagItem("exception.type").Should().Be("InvalidOperationException");
        span.GetTagItem("headless.job.function").Should().Be("Refunds");
    }

    [Fact]
    public void should_leave_a_span_unchanged_when_no_text_redactor_is_registered()
    {
        // when
        var span = _Record(
            activity =>
            {
                activity.SetTag("exception.message", $"no citizen {_NationalId}");
                activity.SetStatus(ActivityStatusCode.Error, $"no citizen {_NationalId}");
            },
            maskText: false
        );

        // then
        span.StatusDescription.Should().Be($"no citizen {_NationalId}");
        span.GetTagItem("exception.message").Should().Be($"no citizen {_NationalId}");
    }

    [Fact]
    public void should_record_an_exception_event_from_the_masked_copy()
    {
        // given: the way AddHeadless records the request span's exception
        using var services = new ServiceCollection()
            .AddHeadlessRedaction(static setup => setup.MaskText(@"\b\d{14}\b"))
            .BuildServiceProvider();
        var masker = services.GetRequiredService<LogTextMasker>();
        Exception original;

        try
        {
            throw new InvalidOperationException("lookup failed", new FormatException($"no citizen {_NationalId}"));
        }
        catch (InvalidOperationException e)
        {
            original = e;
        }

        // when
        var span = _Record(activity => activity.AddException(masker.Mask(original)), maskText: true);

        // then: the event keeps the real type and stack frames, and only the id is masked
        var tags = span.Events.Should().ContainSingle().Which.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags["exception.type"].Should().Be(typeof(InvalidOperationException).FullName);
        tags["exception.message"].Should().Be("lookup failed");
        var stackTrace = tags["exception.stacktrace"].Should().BeOfType<string>().Subject;
        stackTrace
            .Should()
            .NotContain(_NationalId)
            .And.Contain("no citizen ***")
            .And.Contain(nameof(should_record_an_exception_event_from_the_masked_copy));
    }

    [Fact]
    public void should_erase_a_tag_whose_name_is_classified_whatever_its_value_type()
    {
        // when: a tracer built without AddHeadless that adds the processor itself
        var span = _Record(
            activity =>
            {
                activity.SetTag("enduser.email", "jane@example.test");
                activity.SetTag("ENDUSER.ID", 42);
                activity.SetTag("app.recipients", new[] { "a@example.test", "b@example.test" });
                activity.SetTag("app.tier", "gold");
            },
            maskText: false,
            static setup =>
                setup
                    .ClassifyProperty("enduser.email", HeadlessDataClassifications.Private)
                    .ClassifyProperty("enduser.id", HeadlessDataClassifications.Identifier)
                    .ClassifyProperty("app.recipients", HeadlessDataClassifications.Private)
        );

        // then: the name matches ignoring case, a collection is redacted element by element, the rest is kept
        span.GetTagItem("enduser.email").Should().Be(string.Empty);
        span.GetTagItem("ENDUSER.ID").Should().Be(string.Empty);
        span.GetTagItem("app.recipients").Should().BeEquivalentTo(new[] { string.Empty, string.Empty });
        span.GetTagItem("app.tier").Should().Be("gold");
    }

    [Fact]
    public void should_erase_every_occurrence_of_a_classified_tag_added_more_than_once()
    {
        // when: AddTag keeps both values under one key, where SetTag would only replace the first
        var span = _Record(
            activity =>
            {
                activity.AddTag("app.tier", "gold");
                activity.AddTag("enduser.email", "a@example.test");
                activity.AddTag("enduser.email", "b@example.test");
            },
            maskText: false,
            static setup => setup.ClassifyProperty("enduser.email", HeadlessDataClassifications.Private)
        );

        // then: the exporter sees no raw value under the key, and the other tag is kept
        span.TagObjects.Where(t => string.Equals(t.Key, "enduser.email", StringComparison.Ordinal))
            .Select(t => t.Value)
            .Should()
            .Equal(string.Empty, string.Empty);
        span.TagObjects.Select(t => t.Value as string)
            .Should()
            .NotContain(value => value != null && value.Contains('@'));
        span.GetTagItem("app.tier").Should().Be("gold");
    }

    [Fact]
    public void should_mask_a_known_shape_in_any_string_tag_and_keep_a_numeric_tag_typed()
    {
        // when
        var span = _Record(
            activity =>
            {
                activity.SetTag("app.national_id", _NationalId);
                activity.SetTag("http.response.status_code", 200);
            },
            maskText: true
        );

        // then
        span.GetTagItem("app.national_id").Should().Be("***");
        span.GetTagItem("http.response.status_code").Should().Be(200);
    }

    private static Activity _Record(
        Action<Activity> act,
        bool maskText,
        Action<HeadlessRedactionSetupBuilder>? configure = null
    )
    {
        var spans = new List<Activity>();

        using (
            var provider = Sdk.CreateTracerProviderBuilder()
                .ConfigureServices(collection =>
                    collection.AddHeadlessRedaction(setup =>
                    {
                        if (maskText)
                        {
                            setup.MaskText(@"\b\d{14}\b");
                        }

                        configure?.Invoke(setup);
                    })
                )
                .AddSource(_SourceName)
                .AddHeadlessTextRedaction()
                .AddInMemoryExporter(spans)
                .Build()
        )
        {
            using var source = new ActivitySource(_SourceName);
            using var activity = source.StartActivity("work");
            act(activity!);
        }

        return spans.Should().ContainSingle().Subject;
    }
}
