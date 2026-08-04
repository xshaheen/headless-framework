// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging.Enrichers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Tests.Enrichers;

public sealed class SanitizedHeaderEnricherTests : TestBase
{
    private const string _HeaderName = "X-Trace-Id";
    private const string _PropertyName = "XTraceId";

    #region Sanitization

    [Theory]
    // Lone BEL with no escape sequence is a bare control character, so it is stripped.
    [InlineData("a\u0007b", "ab")]
    // Lone ESC with no CSI/OSC terminator falls through to the control-character pass.
    [InlineData("\u001babc", "abc")]
    // Tab is the one control character the sanitizer preserves.
    [InlineData("a\tb", "a\tb")]
    [InlineData("line\r\nbreak", "linebreak")]
    [InlineData("\rleading", "leading")]
    // ANSI CSI colour sequence.
    [InlineData("\u001b[31mred\u001b[0m", "red")]
    // ANSI OSC window-title sequence, terminated by BEL.
    [InlineData("\u001b]0;title\u0007after", "after")]
    // NUL alongside a CSI sequence and a newline: every pass contributes.
    [InlineData("mixed\u001b[1m\r\n\u0000value", "mixedvalue")]
    [InlineData("plain-value", "plain-value")]
    public void should_sanitize_header_value(string rawValue, string expected)
    {
        var httpContext = _CreateHttpContext(rawValue);
        var enricher = _CreateEnricher(httpContext);
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, _PropertyName).Should().Be(expected);
    }

    [Fact]
    public void should_reuse_original_instance_when_value_is_clean_and_within_max_length()
    {
        // Pins the SearchValues fast path: a value carrying no strippable character must skip the
        // replace/regex passes entirely and hand back the very string the header holds.
        var rawValue = new string('a', 32);
        var httpContext = _CreateHttpContext(rawValue);
        var enricher = _CreateEnricher(httpContext);
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, _PropertyName).Should().BeSameAs(rawValue);
    }

    [Fact]
    public void should_truncate_when_clean_value_exceeds_max_length()
    {
        var httpContext = _CreateHttpContext("abcdefgh");
        var enricher = _CreateEnricher(httpContext, maxLength: 5);
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, _PropertyName).Should().Be("abcde");
    }

    [Fact]
    public void should_sanitize_before_truncating_when_dirty_value_exceeds_max_length()
    {
        // Truncating first would yield "\r\nabc"; sanitizing first yields "abcde".
        var httpContext = _CreateHttpContext("\r\nabcdefgh");
        var enricher = _CreateEnricher(httpContext, maxLength: 5);
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, _PropertyName).Should().Be("abcde");
    }

    #endregion

    #region Property Naming

    [Fact]
    public void should_derive_property_name_from_header_name_when_property_name_omitted()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "curl/8.0";
        var enricher = new SanitizedHeaderEnricher(new StubHttpContextAccessor(httpContext), "User-Agent");
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, "UserAgent").Should().Be("curl/8.0");
    }

    [Fact]
    public void should_use_explicit_property_name_when_provided()
    {
        var httpContext = _CreateHttpContext("value");
        var enricher = new SanitizedHeaderEnricher(
            new StubHttpContextAccessor(httpContext),
            _HeaderName,
            propertyName: "CorrelationId"
        );
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, "CorrelationId").Should().Be("value");
        logEvent.Properties.Should().NotContainKey(_PropertyName);
    }

    #endregion

    #region No-op Paths

    [Fact]
    public void should_not_add_property_when_header_absent()
    {
        var enricher = _CreateEnricher(new DefaultHttpContext());
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        logEvent.Properties.Should().BeEmpty();
    }

    [Fact]
    public void should_not_add_property_when_header_value_empty()
    {
        var enricher = _CreateEnricher(_CreateHttpContext(""));
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        logEvent.Properties.Should().BeEmpty();
    }

    [Fact]
    public void should_not_add_property_when_no_http_context()
    {
        var enricher = new SanitizedHeaderEnricher(new StubHttpContextAccessor(httpContext: null), _HeaderName);
        var logEvent = _CreateLogEvent();

        enricher.Enrich(logEvent, _PropertyFactory);

        logEvent.Properties.Should().BeEmpty();
    }

    #endregion

    #region Per-request Behavior

    [Fact]
    public void should_not_interfere_when_two_enrichers_read_different_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Trace-Id"] = "trace-1";
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-1";
        var accessor = new StubHttpContextAccessor(httpContext);
        var traceEnricher = new SanitizedHeaderEnricher(accessor, "X-Trace-Id");
        var tenantEnricher = new SanitizedHeaderEnricher(accessor, "X-Tenant-Id");
        var logEvent = _CreateLogEvent();

        traceEnricher.Enrich(logEvent, _PropertyFactory);
        tenantEnricher.Enrich(logEvent, _PropertyFactory);

        _GetPropertyValue(logEvent, "XTraceId").Should().Be("trace-1");
        _GetPropertyValue(logEvent, "XTenantId").Should().Be("tenant-1");
    }

    [Fact]
    public void should_enrich_every_event_when_a_request_logs_more_than_once()
    {
        var enricher = _CreateEnricher(_CreateHttpContext("trace-1"));
        var first = _CreateLogEvent();
        var second = _CreateLogEvent();

        enricher.Enrich(first, _PropertyFactory);
        enricher.Enrich(second, _PropertyFactory);

        _GetPropertyValue(first, _PropertyName).Should().Be("trace-1");
        _GetPropertyValue(second, _PropertyName).Should().Be("trace-1");
    }

    [Fact]
    public void should_observe_header_stamped_after_the_first_event()
    {
        // Middleware that stamps the header mid-pipeline must still be picked up: the enricher may not
        // cache "header absent" for the lifetime of the request.
        var httpContext = new DefaultHttpContext();
        var enricher = _CreateEnricher(httpContext);
        var beforeStamp = _CreateLogEvent();
        var afterStamp = _CreateLogEvent();

        enricher.Enrich(beforeStamp, _PropertyFactory);
        httpContext.Request.Headers[_HeaderName] = "trace-late";
        enricher.Enrich(afterStamp, _PropertyFactory);

        beforeStamp.Properties.Should().BeEmpty();
        _GetPropertyValue(afterStamp, _PropertyName).Should().Be("trace-late");
    }

    [Fact]
    public void should_not_write_to_http_context_items()
    {
        // HttpContext.Items is a non-thread-safe dictionary shared with the rest of the pipeline;
        // the enricher runs on arbitrary logging threads and must leave it alone.
        var httpContext = _CreateHttpContext("trace-1");
        var enricher = _CreateEnricher(httpContext);

        enricher.Enrich(_CreateLogEvent(), _PropertyFactory);
        enricher.Enrich(_CreateLogEvent(), _PropertyFactory);

        httpContext.Items.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static readonly MessageTemplateParser _TemplateParser = new();
    private static readonly TestPropertyFactory _PropertyFactory = new();

    private static DefaultHttpContext _CreateHttpContext(string headerValue)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[_HeaderName] = headerValue;

        return httpContext;
    }

    private static SanitizedHeaderEnricher _CreateEnricher(HttpContext httpContext, int maxLength = 512)
    {
        return new SanitizedHeaderEnricher(
            new StubHttpContextAccessor(httpContext),
            _HeaderName,
            propertyName: null,
            maxLength
        );
    }

    private static LogEvent _CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            _TemplateParser.Parse("test message"),
            properties: []
        );
    }

    private static object? _GetPropertyValue(LogEvent logEvent, string propertyName)
    {
        return logEvent.Properties.TryGetValue(propertyName, out var value) ? ((ScalarValue)value).Value : null;
    }

    private sealed class StubHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private sealed class TestPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }

    #endregion
}
