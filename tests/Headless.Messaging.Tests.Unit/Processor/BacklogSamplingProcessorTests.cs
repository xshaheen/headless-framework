// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Processor;

// The backlog gauges report every sampler registered in the process, and xUnit runs test classes in parallel, so no
// assertion here reads the gauges unfiltered: a test checks its own sampler's measurements, or looks for readings that
// carry a value only its own sampler reports.
public sealed class BacklogSamplingProcessorTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_report_no_measurement_when_no_sample_has_been_taken()
    {
        // given
        await using var host = _CreateInMemoryHost();
        await host.Storage.StoreMessageAsync("backlog.unsampled", _CreateMessage(), cancellationToken: AbortToken);

        // when
        using var sampler = host.CreateSampler();

        // then
        sampler.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task should_measure_pending_count_and_oldest_age_per_table_when_a_sample_is_taken()
    {
        // given: one published row stored three minutes ago and one received row stored one minute ago
        await using var host = _CreateInMemoryHost();
        await host.Storage.StoreMessageAsync("backlog.published", _CreateMessage(), cancellationToken: AbortToken);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await host.Storage.StoreReceivedMessageAsync("backlog.received", "backlog-group", _CreateMessage(), AbortToken);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var sampler = host.CreateSampler();

        // when
        await sampler.SampleAsync(AbortToken);

        // then: each table reports its count and the wait of its oldest row, tagged by direction only
        sampler
            .Measurements.Select(m => (m.Pending, m.OldestAgeSeconds, Tags: _TagText(m.Tags)))
            .Should()
            .Equal(
                (1L, 180d, $"{MessagingMetrics.TagPersistenceType}={MessagingMetrics.PersistenceTypePublish}"),
                (1L, 60d, $"{MessagingMetrics.TagPersistenceType}={MessagingMetrics.PersistenceTypeConsume}")
            );
    }

    [Fact]
    public async Task should_measure_zero_age_when_no_row_is_waiting()
    {
        // given
        await using var host = _CreateInMemoryHost();
        using var sampler = host.CreateSampler();

        // when
        await sampler.SampleAsync(AbortToken);

        // then
        sampler.Measurements.Select(m => (m.Pending, m.OldestAgeSeconds)).Should().Equal((0L, 0d), (0L, 0d));
    }

    [Fact]
    public async Task should_report_the_last_sample_on_the_gauges_when_they_are_collected()
    {
        // given: a sample whose count and age no other sampler in the process reports
        var pending = _UniqueValue();
        var age = TimeSpan.FromSeconds(_UniqueValue());
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .GetBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(_Backlog(published: pending, publishedAge: age));
        using var sampler = _CreateSampler(monitoring);
        await sampler.SampleAsync(AbortToken);

        // when
        var readings = _CollectGauges();

        // then
        readings
            .Should()
            .ContainEquivalentOf(
                new GaugeReading(MessagingMetrics.BacklogPendingName, pending, MessagingMetrics.PersistenceTypePublish)
            )
            .And.ContainEquivalentOf(
                new GaugeReading(
                    MessagingMetrics.BacklogOldestAgeName,
                    age.TotalSeconds,
                    MessagingMetrics.PersistenceTypePublish
                )
            );
    }

    [Fact]
    public async Task should_take_the_first_sample_one_interval_after_processing_starts()
    {
        // given
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring.GetBacklogAsync(Arg.Any<CancellationToken>()).Returns(_Backlog(published: 4));
        var clock = new FakeTimeProvider();
        using var sampler = _CreateSampler(monitoring);
        await using var context = new ProcessingContext(_EmptyProvider(), clock, AbortToken);

        // when: the tick starts, then the clock reaches the interval
        var tick = sampler.ProcessAsync(context);
        await monitoring.DidNotReceive().GetBacklogAsync(Arg.Any<CancellationToken>());
        clock.Advance(_Interval);
        await tick.Bounded();

        // then
        await monitoring.Received(1).GetBacklogAsync(Arg.Any<CancellationToken>());
        sampler.Measurements.Select(m => m.Pending).Should().Equal(4, 0);
    }

    [Fact]
    public async Task should_keep_the_last_sample_when_a_read_fails()
    {
        // given: the first read succeeds and the next one fails
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .GetBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => _Backlog(published: 3, publishedAge: TimeSpan.FromSeconds(42)),
                _ => throw new InvalidOperationException("storage unreachable")
            );
        using var sampler = _CreateSampler(monitoring);
        await sampler.SampleAsync(AbortToken);
        var first = sampler.Measurements.ToArray();

        // when
        var act = () => sampler.SampleAsync(AbortToken);

        // then: the failure is absorbed and the gauges keep the earlier reading
        await act.Should().NotThrowAsync();
        sampler.Measurements.Should().Equal(first);
        sampler.Measurements[0].Pending.Should().Be(3);
        sampler.Measurements[0].OldestAgeSeconds.Should().Be(42);
    }

    [Fact]
    public async Task should_propagate_cancellation_when_the_sample_is_canceled()
    {
        // given
        using var cancellation = new CancellationTokenSource();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .GetBacklogAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<BacklogView>>(call =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });
        using var sampler = _CreateSampler(monitoring);

        // when
        var act = () => sampler.SampleAsync(cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_report_nothing_on_the_gauges_when_the_sampler_is_disposed()
    {
        // given
        var pending = _UniqueValue();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring.GetBacklogAsync(Arg.Any<CancellationToken>()).Returns(_Backlog(published: pending));
        var sampler = _CreateSampler(monitoring);
        await sampler.SampleAsync(AbortToken);
        _CollectGauges().Should().Contain(reading => reading.Value == pending);

        // when
        sampler.Dispose();

        // then
        _CollectGauges().Should().NotContain(reading => reading.Value == pending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_run_the_sampler_only_when_an_interval_is_set(bool enabled)
    {
        // given: a sampler registration that records whether the processing server asked for it
        var resolved = false;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton(sp =>
        {
            resolved = true;
            return ActivatorUtilities.CreateInstance<BacklogSamplingProcessor>(sp);
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.BacklogSamplingInterval = enabled ? _Interval : null;
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetServices<IProcessingServer>().OfType<MessageProcessingServer>().Single();

        // when
        await server.StartAsync(AbortToken);
        await ((IProcessingServerShutdown)server).StopAsync(TimeSpan.FromSeconds(5));

        // then
        resolved.Should().Be(enabled);
    }

    private static InMemoryHost _CreateInMemoryHost()
    {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.BacklogSamplingInterval = _Interval;
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        return new InMemoryHost(services.BuildServiceProvider(), clock);
    }

    private static BacklogSamplingProcessor _CreateSampler(IMonitoringApi monitoring)
    {
        var storage = Substitute.For<IDataStorage>();
        storage.GetMonitoringApi().Returns(monitoring);
        var services = new ServiceCollection();
        services.AddSingleton(storage);

        return new BacklogSamplingProcessor(
            NullLogger<BacklogSamplingProcessor>.Instance,
            Options.Create(new MessagingOptions { BacklogSamplingInterval = _Interval }),
            services.BuildServiceProvider()
        );
    }

    // Large enough that no other test's backlog reports it, small enough to stay exact as a double.
    private static long _UniqueValue() => 1_000_000 + (Guid.NewGuid().GetHashCode() & 0x3FFF_FFFF);

    private static string _TagText(KeyValuePair<string, object?>[] tags) =>
        string.Join(',', tags.Select(tag => $"{tag.Key}={tag.Value}"));

    private static IServiceProvider _EmptyProvider() => new ServiceCollection().BuildServiceProvider();

    private static ValueTask<BacklogView> _Backlog(long published, TimeSpan? publishedAge = null)
    {
        return ValueTask.FromResult(
            new BacklogView
            {
                PublishedPending = published,
                ReceivedPending = 0,
                OldestPublishedPendingAge = publishedAge,
            }
        );
    }

    private static Message _CreateMessage()
    {
        return new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString(),
                [Headers.MessageName] = "backlog.test",
            },
            "{}"
        );
    }

    private static List<GaugeReading> _CollectGauges()
    {
        var readings = new List<GaugeReading>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name is MessagingMetrics.BacklogPendingName or MessagingMetrics.BacklogOldestAgeName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => capture(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => capture(instrument, value, tags));
        listener.Start();
        listener.RecordObservableInstruments();

        return readings;

        void capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? direction = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, MessagingMetrics.TagPersistenceType, StringComparison.Ordinal))
                {
                    direction = tag.Value as string;
                }
            }

            readings.Add(new GaugeReading(instrument.Name, value, direction));
        }
    }

    private sealed record GaugeReading(string Instrument, double Value, string? Direction);

    private sealed class InMemoryHost(ServiceProvider provider, FakeTimeProvider clock) : IAsyncDisposable
    {
        public FakeTimeProvider Clock { get; } = clock;

        public IDataStorage Storage { get; } = provider.GetRequiredService<IDataStorage>();

        public BacklogSamplingProcessor CreateSampler() =>
            ActivatorUtilities.CreateInstance<BacklogSamplingProcessor>(provider);

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
