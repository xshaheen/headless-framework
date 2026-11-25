// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// A rejected token logs the siteverify response as separate <c>response.*</c> properties. The message text must not
/// also print the response object, whose <see cref="object.ToString"/> is only its type name.
/// </summary>
public sealed class ReCaptchaFailureLogTests : TestBase
{
    private readonly ReCaptchaV2VerifierFixture _v2Fixture = new();
    private readonly ReCaptchaV3VerifierFixture _v3Fixture = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _v2Fixture.Dispose();
        _v3Fixture.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_log_v2_failure_as_response_properties_without_the_type_name_in_the_message()
    {
        using var capture = new CapturingLoggerProvider();
        using var handler = new StubSiteVerifyHandler();
        var stub = handler.EnqueueJson(HttpStatusCode.OK, _v2Fixture.RejectedResponseBody);
        var verifier = _v2Fixture.CreateVerifierWithServices(stub, s => s.AddLogging(b => b.AddProvider(capture)));

        await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        var entry = capture.Entries.Single(e =>
            string.Equals(e.EventId.Name, "ReCaptchaV2Failure", StringComparison.Ordinal)
        );
        entry.Message.Should().Be("[reCAPTCHA] V2 validation failed");
        entry.Properties.Should().NotContain(p => p.Key == "Response");
        entry.Properties.Should().Contain(p => p.Key == "response.Success" && Equals(p.Value, false));
        // [LogProperties] flattens a collection member into one string property.
        entry
            .Properties.Should()
            .Contain(p =>
                p.Key == "response.ErrorCodes"
                && string.Equals(p.Value as string, """["invalid-input-response"]""", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_log_v3_failure_as_response_properties_without_the_type_name_in_the_message()
    {
        using var capture = new CapturingLoggerProvider();
        using var handler = new StubSiteVerifyHandler();
        var stub = handler.EnqueueJson(HttpStatusCode.OK, _v3Fixture.RejectedResponseBody);
        var verifier = _v3Fixture.CreateVerifierWithServices(stub, s => s.AddLogging(b => b.AddProvider(capture)));

        await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        var entry = capture.Entries.Single(e =>
            string.Equals(e.EventId.Name, "ReCaptchaV3Failure", StringComparison.Ordinal)
        );
        entry.Message.Should().Be("[reCAPTCHA] V3 validation failed");
        entry.Properties.Should().NotContain(p => p.Key == "Response");
        entry.Properties.Should().Contain(p => p.Key == "response.Success" && Equals(p.Value, false));
        // [LogProperties] flattens a collection member into one string property.
        entry
            .Properties.Should()
            .Contain(p =>
                p.Key == "response.ErrorCodes"
                && string.Equals(p.Value as string, """["timeout-or-duplicate"]""", StringComparison.Ordinal)
            );
    }

    private sealed record LogEntry(
        EventId EventId,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties
    );

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];
        private readonly Lock _lock = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private void _Add(LogEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
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
                var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? [.. pairs]
                    : Array.Empty<KeyValuePair<string, object?>>();
                owner._Add(new LogEntry(eventId, formatter(state, exception), properties));
            }
        }
    }
}
