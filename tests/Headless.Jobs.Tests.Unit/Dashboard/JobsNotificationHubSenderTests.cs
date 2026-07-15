// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Hubs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Tests.Dashboard;

public sealed class JobsNotificationHubSenderTests : TestBase
{
    [Fact]
    public async Task should_log_fire_and_forget_signalr_send_failures()
    {
        var logger = new CapturingLogger<JobsNotificationHubSender>();
        var all = Substitute.For<IClientProxy>();
        var failure = new InvalidOperationException("send failed");

        all.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        all.SendCoreAsync("GetHostStatusNotification", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));

        using var sender = _Create(all, logger);

        sender.UpdateHostStatus("active");

        var entry = await logger.WaitForFirstEntryAsync(AbortToken);

        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Should().Be(2010);
        entry.Exception.Should().BeSameAs(failure);
        entry.Message.Should().Contain("GetHostStatusNotification");
    }

    private static JobsNotificationHubSender _Create(
        IClientProxy all,
        CapturingLogger<JobsNotificationHubSender> logger
    )
    {
        var hubContext = Substitute.For<IHubContext<JobsNotificationHub>>();
        var clients = Substitute.For<IHubClients>();

        hubContext.Clients.Returns(clients);
        clients.All.Returns(all);

        return new JobsNotificationHubSender(hubContext, logger);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly TaskCompletionSource<LogEntry> _firstEntry = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var entry = new LogEntry(logLevel, eventId.Id, formatter(state, exception), exception);

            Entries.Enqueue(entry);
            _firstEntry.TrySetResult(entry);
        }

        public async Task<LogEntry> WaitForFirstEntryAsync(CancellationToken cancellationToken)
        {
            return await _firstEntry.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed record LogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);
}
