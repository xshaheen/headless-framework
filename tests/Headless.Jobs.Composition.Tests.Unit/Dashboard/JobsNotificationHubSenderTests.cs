// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;
using Headless.Jobs.Entities;
using Headless.Jobs.Hubs;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

public sealed class JobsNotificationHubSenderTests : TestBase
{
    [Fact]
    public async Task live_occurrence_payload_preserves_contract_lineage_separately_from_retry_count()
    {
        var hubContext = Substitute.For<IHubContext<JobsNotificationHub>>();
        var clients = Substitute.For<IHubClients>();
        var group = Substitute.For<IClientProxy>();
        var parentId = Guid.NewGuid();
        hubContext.Clients.Returns(clients);
        clients.Group(parentId.ToString()).Returns(group);
        JsonElement payload = default;
        group
            .SendCoreAsync(
                "UpdateCronOccurrenceNotification",
                Arg.Do<object?[]>(args => payload = JsonSerializer.SerializeToElement(args[0])),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        using var sender = new JobsNotificationHubSender(
            hubContext,
            new CapturingLogger<JobsNotificationHubSender>(),
            new FakeTimeProvider()
        );
        var state = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            ParentId = parentId,
            FunctionName = "snapshot.name",
            ContractVersion = "v1",
            CorrelationId = "business-root",
            CausationId = "direct-cause",
            TenantId = null,
            RetryCount = 7,
        };

        await sender.UpdateCronOccurrenceFromExecutionState<CronJobEntity>(state);

        payload.GetProperty("function").GetString().Should().Be("snapshot.name");
        payload.GetProperty("contractVersion").GetString().Should().Be("v1");
        payload.GetProperty("correlationId").GetString().Should().Be("business-root");
        payload.GetProperty("causationId").GetString().Should().Be("direct-cause");
        payload.GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("retryCount").GetInt32().Should().Be(7);
        payload.GetProperty("id").GetGuid().Should().Be(state.JobId);
        payload.TryGetProperty("generation", out _).Should().BeFalse();
    }

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

    [Fact]
    public async Task should_debounce_time_job_updates_with_the_injected_time_provider()
    {
        var logger = new CapturingLogger<JobsNotificationHubSender>();
        var all = Substitute.For<IClientProxy>();
        all.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var timeProvider = new FakeTimeProvider();
        using var sender = _Create(all, logger, timeProvider);

        await sender.UpdateTimeJobFromExecutionState<TimeJobEntity>(new JobExecutionState { FunctionName = "test" });
        await sender.UpdateTimeJobFromExecutionState<TimeJobEntity>(new JobExecutionState { FunctionName = "test" });
        timeProvider.Advance(TimeSpan.FromMilliseconds(99));

        await all.DidNotReceive()
            .SendCoreAsync("UpdateTimeJobNotification", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await all.Received(1)
            .SendCoreAsync("UpdateTimeJobNotification", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    private static JobsNotificationHubSender _Create(
        IClientProxy all,
        CapturingLogger<JobsNotificationHubSender> logger,
        TimeProvider? timeProvider = null
    )
    {
        var hubContext = Substitute.For<IHubContext<JobsNotificationHub>>();
        var clients = Substitute.For<IHubClients>();

        hubContext.Clients.Returns(clients);
        clients.All.Returns(all);

        return new JobsNotificationHubSender(hubContext, logger, timeProvider ?? new FakeTimeProvider());
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
