// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// The operator log: the default sink writes each entry as one structured <c>ILogger</c> event, and every registered
/// sink receives every entry.
/// </summary>
public sealed class OperatorLogTests : TestBase
{
    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();

    [Fact]
    public async Task should_write_one_structured_information_event_per_entry_with_every_field()
    {
        using var capture = new CapturingLoggerProvider();
        using var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(capture));
        var sink = new LoggerOperatorLog(factory);

        await sink.WriteAsync(
            new OperatorLogEntry
            {
                Timestamp = _time.GetUtcNow(),
                Actor = "alice",
                Channel = DashboardChannels.Api,
                Module = "diag",
                CommandId = "diag.requeue",
                Action = OperatorLogAction.OperationChunk,
                Ids = ["run-1", "run-2"],
                Scope = """{"queue":"default"}""",
                Count = 2,
                Reason = "incident 42",
                Done = 1,
                Skipped = 1,
                OperationId = "op-1",
            },
            AbortToken
        );

        var record = capture.Records.Should().ContainSingle().Which;
        record.Category.Should().Be(LoggerOperatorLog.Category);
        record.Level.Should().Be(LogLevel.Information);
        record.EventId.Name.Should().Be("DashboardOperatorAction");
        record.State["Actor"].Should().Be("alice");
        record.State["Channel"].Should().Be(DashboardChannels.Api);
        record.State["Action"].Should().Be(OperatorLogAction.OperationChunk);
        record.State["CommandId"].Should().Be("diag.requeue");
        record.State["ModuleId"].Should().Be("diag");
        record.State["Count"].Should().Be(2L);
        record.State["Done"].Should().Be(1L);
        record.State["Skipped"].Should().Be(1L);
        record.State["Failed"].Should().Be(0L);
        record.State["Reason"].Should().Be("incident 42");
        record.State["OperationId"].Should().Be("op-1");
        record.State["Ids"].Should().BeEquivalentTo(new[] { "run-1", "run-2" });
        record.State["Scope"].Should().Be("""{"queue":"default"}""");
        record.Message.Should().Contain("alice").And.Contain("diag.requeue");
    }

    [Fact]
    public async Task should_send_every_entry_to_the_default_logger_sink_and_to_each_host_sink()
    {
        var store = new FakeRunStore();
        store.SeedFailed(3);
        var hostSink = new RecordingOperatorLog();
        using var capture = new CapturingLoggerProvider();
        await using var app = await CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = store,
                Time = _time,
                Log = hostSink,
                ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture),
            },
            AbortToken
        );
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer diag-operator", AbortToken);

        using var response = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new
            {
                ids = new[] { "run-000001" },
                reason = "retry",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        hostSink.Entries.Should().ContainSingle().Which.CommandId.Should().Be(FakeCommandModule.Requeue);
        var record = capture
            .Records.Where(static r => string.Equals(r.Category, LoggerOperatorLog.Category, StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Which;
        record.State["Action"].Should().Be(OperatorLogAction.Run);
        record.State["Actor"].Should().Be("diag-operator");
        record.State["Channel"].Should().Be(DashboardChannels.Ui);
    }

    /// <summary>A logger provider that keeps every event with its structured state.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly Lock _lock = new();
        private readonly List<LogRecord> _records = [];

        public IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_lock)
                {
                    return [.. _records];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private void _Add(LogRecord record)
        {
            lock (_lock)
            {
                _records.Add(record);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
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
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(static p => p.Key, static p => p.Value, StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);

                provider._Add(new LogRecord(category, logLevel, eventId, values, formatter(state, exception)));
            }
        }
    }

    private sealed record LogRecord(
        string Category,
        LogLevel Level,
        EventId EventId,
        IReadOnlyDictionary<string, object?> State,
        string Message
    );
}
