// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed partial class LogTextRedactionTests : TestBase
{
    private const string _NationalId = "29001011234567";

    [Fact]
    public void should_mask_a_matching_value_in_the_text_and_a_string_property()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        LogCitizen(logger, _NationalId, 3);

        // then: the shape is masked wherever it appears, and non-string values keep their type
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Citizen *** has 3 requests");
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("NationalId", "***"));
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("Count", 3));
    }

    [Fact]
    public void should_mask_a_value_baked_into_the_message_text()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when: an interpolated message has no hole, so only text scrubbing can reach the value
        LogRaw(logger, $"lookup failed for {_NationalId}");

        // then
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("lookup failed for ***");
    }

    [Fact]
    public void should_mask_a_matching_value_in_a_scope()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

        // when
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Citizen"] = _NationalId }))
        {
            LogRaw(logger, "inside");
        }

        // then
        sink.Scopes.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(new KeyValuePair<string, object?>("Citizen", "***"));
    }

    [Fact]
    public void should_leave_text_without_a_match_unchanged()
    {
        // given
        var redactor = new RegexLogTextRedactor(_Pattern());
        const string text = "order 42 shipped";

        // when
        var result = redactor.Redact(text);

        // then
        result.Should().BeSameAs(text);
    }

    [Fact]
    public void should_replace_the_whole_text_when_the_pattern_times_out()
    {
        // given: a backtracking pattern that cannot finish within its timeout
        var redactor = new RegexLogTextRedactor(
            new Regex("(?<run>a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1)),
            replacement: "[masked]"
        );

        // when
        var result = redactor.Redact(new string('a', 40_000) + "!");

        // then: a slow match fails closed instead of letting the text through
        result.Should().Be("[masked]");
    }

    [Fact]
    public void should_throw_when_arguments_are_null()
    {
        var nullPattern = () => new RegexLogTextRedactor(null!);
        var nullReplacement = () => new RegexLogTextRedactor(_Pattern(), null!);

        nullPattern.Should().Throw<ArgumentNullException>();
        nullReplacement.Should().Throw<ArgumentNullException>();
    }

    [LoggerMessage(1, LogLevel.Information, "Citizen {NationalId} has {Count} requests")]
    private static partial void LogCitizen(ILogger logger, string nationalId, int count);

    [LoggerMessage(2, LogLevel.Information, "{Text}")]
    private static partial void LogRaw(ILogger logger, string text);

    private static Regex _Pattern() => new(@"\b\d{14}\b", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    private static ServiceProvider _Build(CapturingProvider sink)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(sink));
        services.AddLogTextRedaction(_Pattern());
        services.AddHeadlessLogRedaction();

        return services.BuildServiceProvider();
    }

    private sealed record Entry(string Text, IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public List<Entry> Entries { get; } = [];

        public List<IReadOnlyList<KeyValuePair<string, object?>>> Scopes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private sealed class Logger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? (IReadOnlyList<KeyValuePair<string, object?>>)
                        [.. values.Where(v => !string.Equals(v.Key, "{OriginalFormat}", StringComparison.Ordinal))]
                    : [];

                owner._scopes?.ForEachScope(
                    static (scope, scopes) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                        {
                            scopes.Add([.. values]);
                        }
                    },
                    owner.Scopes
                );

                owner.Entries.Add(new Entry(formatter(state, exception), properties));
            }
        }
    }
}
