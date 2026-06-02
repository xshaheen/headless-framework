// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Fakes;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_lock_provider_without_messaging_and_skip_lock_released_consumer()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedLockProvider>().Should().NotBeNull();
        provider.GetService<IOutboxBus>().Should().BeNull();
        // The LockReleasedConsumer's only job is to wake waiters on DistributedLockReleased outbox
        // messages; without IOutboxBus no such messages ever flow, so the consumer is
        // intentionally NOT registered in polling-only mode.
        services
            .Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
    }

    [Fact]
    public void should_register_lock_released_consumer_when_messaging_wakeups_are_enabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IOutboxBus>());

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        services.AddHeadlessMessaging(setup => setup.UseDistributedLockReleaseWakeups());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedLockProvider>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxBus>().Should().NotBeNull();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));

        var metadata = provider.GetRequiredService<ConsumerRegistry>().GetAll().Single();
        metadata.ConsumerType.Should().Be<DistributedLockProvider.LockReleasedConsumer>();
        metadata.MessageName.Should().Be("headless.locks.released");
        metadata.IntentType.Should().Be(IntentType.Bus);
        metadata.Concurrency.Should().Be(1);
    }

    [Fact]
    public void should_warn_when_outbox_publisher_is_registered_without_release_wakeups()
    {
        // given — messaging is available, but the caller did not opt into the lock-release
        // consumer through setup.UseDistributedLockReleaseWakeups().
        var capturedLogs = new List<(LogLevel Level, EventId EventId)>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(capturedLogs)));

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        services.AddSingleton(Substitute.For<IOutboxBus>());
        using var provider = services.BuildServiceProvider();

        // resolving the options instance triggers the IValidateOptions pipeline (the Headless
        // option-validator helper wires ValidateOnStart, which calls .Value internally at host
        // start; outside a Host we trigger validation explicitly).
        _ = provider.GetRequiredService<DistributedLockOptions>();

        // then — ContainSingle guards against a regression where the validator fires per named
        // options instance (would inflate the warning to N x duplicate noise at startup).
        capturedLogs.Should().ContainSingle(entry => entry.EventId.Id == 18 && entry.Level == LogLevel.Warning);

        // The consumer was NOT registered (proves the warning is justified, not noise).
        services
            .Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IConsume<DistributedLockReleased>));
    }

    [Fact]
    public void should_not_warn_when_release_wakeups_are_enabled()
    {
        // given — messaging is available and the lock-release consumer is explicitly registered.
        var capturedLogs = new List<(LogLevel Level, EventId EventId)>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(capturedLogs)));
        services.AddSingleton(Substitute.For<IOutboxBus>());

        // when
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        services.AddHeadlessMessaging(setup => setup.UseDistributedLockReleaseWakeups());
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<DistributedLockOptions>();

        // then — neither the publisher-absent (EventId 16) nor the consumer-missing (EventId 18)
        // warning fires when messaging wake-ups are configured.
        capturedLogs
            .Should()
            .NotContain(entry => entry.EventId.Id == 18)
            .And.NotContain(entry => entry.EventId.Id == 16);
    }

    [Fact]
    public void should_be_idempotent_for_repeated_add_distributed_lock_calls()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — call AddDistributedLock twice.
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });
        services.AddDistributedLock<FakeDistributedLockStorage>(_ => { });

        // then — only one descriptor per service type (TryAdd* semantics).
        services
            .Count(d => d.ServiceType == typeof(IDistributedLockProvider))
            .Should()
            .Be(1, "TryAddSingleton on IDistributedLockProvider must be idempotent");
        services
            .Count(d => d.ServiceType == typeof(DistributedLockProvider))
            .Should()
            .Be(1, "TryAddSingleton on the concrete DistributedLockProvider must be idempotent");
    }

    /// <summary>
    /// In-memory <see cref="ILoggerProvider"/> capturing <see cref="LogLevel"/> + <see cref="EventId"/>
    /// pairs. Mirrors the messaging tests' CapturingLoggerProvider pattern; assertions stay stable
    /// against message-template changes by ignoring formatted strings.
    /// </summary>
    private sealed class CapturingLoggerProvider(List<(LogLevel Level, EventId EventId)> log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(log);
        }

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId)> log) : ILogger
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
                lock (log)
                {
                    log.Add((logLevel, eventId));
                }
            }
        }
    }
}
