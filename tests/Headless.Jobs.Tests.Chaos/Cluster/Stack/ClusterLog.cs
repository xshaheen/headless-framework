// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests.Cluster;

/// <summary>
/// Keeps the last warnings and errors every host of a run logged, tagged with the incarnation, so a failed run can show
/// what the nodes said around the violation without flooding the output of a passing one.
/// </summary>
public sealed class ClusterLog(JobLedger ledger)
{
    private const int _Capacity = 400;

    private readonly ConcurrentQueue<string> _lines = new();

    public int Count => _lines.Count;

    public ILoggerProvider ProviderFor(string runner) => new Provider(this, runner);

    public string Tail(int lines) => string.Join(Environment.NewLine, _lines.TakeLast(lines));

    private void _Add(string line)
    {
        _lines.Enqueue(line);

        while (_lines.Count > _Capacity && _lines.TryDequeue(out _)) { }
    }

    private sealed class Provider(ClusterLog log, string runner) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(log, runner, categoryName);

        public void Dispose() { }
    }

    private sealed class Logger(ClusterLog log, string runner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = category[(category.LastIndexOf('.', StringComparison.Ordinal) + 1)..];
            log._Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{log._NowSeconds():F2}s {runner} {logLevel} {shortCategory}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message)}"
                )
            );
        }
    }

    private double _NowSeconds() => ledger.Now.TotalSeconds;
}
