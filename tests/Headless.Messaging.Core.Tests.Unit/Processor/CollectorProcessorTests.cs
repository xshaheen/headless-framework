// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Processor;

public sealed class CollectorProcessorTests : TestBase
{
    [Fact]
    public async Task should_visit_history_before_repeating_a_busy_published_batch()
    {
        var storage = Substitute.For<IDataStorage>();
        var clock = new ImmediateTimeProvider();
        var visits = new List<string>();
        var publishedCalls = 0;
        storage
            .DeleteExpiresAsync("published", Arg.Any<DateTimeOffset>(), 1000, AbortToken)
            .Returns(_ =>
            {
                visits.Add("published");
                if (++publishedCalls == 2)
                {
                    throw new InvalidOperationException("bounded stop");
                }

                return 1000;
            });
        storage
            .DeleteExpiresAsync("received", Arg.Any<DateTimeOffset>(), 1000, AbortToken)
            .Returns(_ =>
            {
                visits.Add("received");
                return 0;
            });
        storage
            .DeleteExpiredInboxAuditsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, AbortToken)
            .Returns(_ =>
            {
                visits.Add("audits");
                return 0;
            });
        storage
            .DeleteExpiredInboxReceiptsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, AbortToken)
            .Returns(_ =>
            {
                visits.Add("receipts");
                return 0;
            });
        await using var provider = _CreateProvider(storage, clock);
        await using var context = new ProcessingContext(provider, clock, AbortToken);
        var sut = _CreateCollector(provider);

        var act = () => sut.ProcessAsync(context);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("bounded stop");

        visits.Should().Equal("published", "received", "audits", "receipts", "published");
        clock.Delays.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_reuse_provider_cutoffs_across_rounds_while_scheduling_clock_advances()
    {
        var storage = Substitute.For<IDataStorage>();
        var clock = new ImmediateTimeProvider();
        var messageCutoff = clock.GetUtcNow();
        var providerNow = messageCutoff.AddDays(-5);
        var cutoffs = new InboxHistoryRetentionCutoffs(
            providerNow.AddDays(-7),
            providerNow.AddDays(-90),
            providerNow.AddDays(-7),
            providerNow.AddDays(-30)
        );
        storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken).Returns(cutoffs);
        storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1000, AbortToken).Returns(1000, 0);
        storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1000, AbortToken).Returns(1000, 0);
        await using var provider = _CreateProvider(storage, clock);
        await using var context = new ProcessingContext(provider, clock, AbortToken);

        await _CreateCollector(provider).ProcessAsync(context);

        await storage.Received(1).GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        await storage.Received(2).DeleteExpiredInboxAuditsAsync(cutoffs, 1000, AbortToken);
        await storage.Received(2).DeleteExpiredInboxReceiptsAsync(cutoffs, 1000, AbortToken);
        await storage.Received(2).DeleteExpiresAsync("published", messageCutoff, 1000, AbortToken);
        await storage.Received(2).DeleteExpiresAsync("received", messageCutoff, 1000, AbortToken);
        clock.Delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(37));
        clock.GetUtcNow().Should().BeAfter(messageCutoff);
    }

    [Fact]
    public async Task should_visit_all_empty_categories_then_wait_for_configured_interval()
    {
        var storage = Substitute.For<IDataStorage>();
        var clock = new ImmediateTimeProvider();
        await using var provider = _CreateProvider(storage, clock);
        await using var context = new ProcessingContext(provider, clock, AbortToken);

        await _CreateCollector(provider).ProcessAsync(context);

        await storage.Received(1).DeleteExpiresAsync("published", Arg.Any<DateTimeOffset>(), 1000, AbortToken);
        await storage.Received(1).DeleteExpiresAsync("received", Arg.Any<DateTimeOffset>(), 1000, AbortToken);
        await storage
            .Received(1)
            .DeleteExpiredInboxAuditsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, AbortToken);
        await storage
            .Received(1)
            .DeleteExpiredInboxReceiptsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, AbortToken);
        clock.Delays.Should().Equal(TimeSpan.FromSeconds(37));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task should_not_start_next_batch_when_cancelled(int deletedCount)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var cancellationToken = cancellation.Token;
        var storage = Substitute.For<IDataStorage>();
        var clock = new ImmediateTimeProvider();
        storage
            .DeleteExpiresAsync("published", Arg.Any<DateTimeOffset>(), 1000, cancellationToken)
            .Returns<ValueTask<int>>(async _ =>
            {
                await cancellation.CancelAsync();
                return deletedCount;
            });
        await using var provider = _CreateProvider(storage, clock);
        await using var context = new ProcessingContext(provider, clock, cancellation.Token);

        var act = () => _CreateCollector(provider).ProcessAsync(context);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await storage
            .DidNotReceive()
            .DeleteExpiresAsync("received", Arg.Any<DateTimeOffset>(), 1000, cancellation.Token);
        await storage
            .DidNotReceive()
            .DeleteExpiredInboxAuditsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, cancellation.Token);
        await storage
            .DidNotReceive()
            .DeleteExpiredInboxReceiptsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, cancellation.Token);
    }

    [Fact]
    public async Task should_log_history_failure_and_allow_infinite_retry_processor_to_retry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var cancellationToken = cancellation.Token;
        var storage = Substitute.For<IDataStorage>();
        var clock = new ImmediateTimeProvider();
        var logger = Substitute.For<ILogger<CollectorProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var failure = new InvalidOperationException("history unavailable");
        var calls = 0;
        storage
            .DeleteExpiredInboxAuditsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, cancellationToken)
            .Returns<ValueTask<int>>(async _ =>
            {
                if (++calls == 1)
                {
                    throw failure;
                }

                await cancellation.CancelAsync();
                return 0;
            });
        await using var provider = _CreateProvider(storage, clock);
        await using var context = new ProcessingContext(provider, clock, cancellation.Token);
        var collector = new CollectorProcessor(logger, Options.Create(new MessagingOptions()), provider);

        await new InfiniteRetryProcessor(collector, LoggerFactory).ProcessAsync(context);

        calls.Should().Be(2);
        await storage.Received(2).GetInboxHistoryRetentionCutoffsAsync(cancellation.Token);
        await storage
            .DidNotReceive()
            .DeleteExpiredInboxReceiptsAsync(Arg.Any<InboxHistoryRetentionCutoffs>(), 1000, cancellation.Token);
        clock
            .Delays.Should()
            .ContainSingle()
            .Which.Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .And.BeLessThan(TimeSpan.FromSeconds(1.25));
        var error = logger
            .ReceivedCalls()
            .Single(call =>
                string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Error
            );
        error.GetArguments()[3].Should().BeSameAs(failure);
        error.GetArguments()[2]!.ToString().Should().Contain("category InboxAudits");
    }

    private static ServiceProvider _CreateProvider(IDataStorage storage, TimeProvider clock)
    {
        var initializer = Substitute.For<IStorageInitializer>();
        initializer.GetPublishedTableName().Returns("published");
        initializer.GetReceivedTableName().Returns("received");
        return new ServiceCollection()
            .AddSingleton(storage)
            .AddSingleton(clock)
            .AddSingleton(initializer)
            .BuildServiceProvider();
    }

    private static CollectorProcessor _CreateCollector(IServiceProvider provider) =>
        new(
            NullLogger<CollectorProcessor>.Instance,
            Options.Create(new MessagingOptions { CollectorCleaningInterval = 37 }),
            provider
        );

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

        public List<TimeSpan> Delays { get; } = [];

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            _now += dueTime;
            callback(state);
            return Substitute.For<ITimer>();
        }
    }
}
