// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_resolve_the_local_release_signal_and_contribute_no_messaging_when_no_transport_is_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then - releases wake only this process's waiters; the lock package declares no messaging
        provider.GetRequiredService<IDistributedLock>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedLockReleaseSignal>()
            .Should()
            .BeOfType<LocalDistributedLockReleaseSignal>();
        services
            .Should()
            .NotContain(static d =>
                string.Equals(d.ServiceType.Name, "MessagingModuleContribution", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void should_reference_no_messaging_assembly_from_the_lock_package()
    {
        // when
        var referenced = typeof(IDistributedLockReleaseSignal).Assembly.GetReferencedAssemblies();

        // then - the cross-host wake-up lives in a bridge package, so the lock package can ship without messaging
        referenced
            .Should()
            .NotContain(static name => name.Name!.StartsWith("Headless.Messaging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_announce_each_release_through_a_release_signal_registered_before_the_locks()
    {
        // given - an application transport registered first, which the default must not replace
        var signal = Substitute.For<IDistributedLockReleaseSignal>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        handle.Should().NotBeNull();

        // when
        await locks.ReleaseAsync(resource, handle!.LeaseId, AbortToken);

        // then
        signal.Received(1).Announce(resource, handle.LeaseId);
    }

    [Fact]
    public void should_apply_a_cross_cutting_extension_after_the_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        var replacement = Substitute.For<IDistributedLockReleaseSignal>();

        // when - registered before the provider in the callback, but applied after it
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.RegisterCrossCuttingExtension(s => s.Replace(ServiceDescriptor.Singleton(replacement)));
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then - the cross-cutting extension replaced the default the provider registered, and did not count as a
        // second provider
        provider.GetRequiredService<IDistributedLockReleaseSignal>().Should().BeSameAs(replacement);
    }

    [Fact]
    public async Task should_not_log_the_absent_cross_host_signal_when_the_provider_runs_in_one_process()
    {
        // given
        using var logs = new EventNameLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        handle.Should().NotBeNull();

        // when
        var act = async () => await locks.ReleaseAsync(resource, handle!.LeaseId, AbortToken);

        // then — an in-process lock has no other host to reach, so there is nothing to tell the operator.
        await act.Should().NotThrowAsync();
        logs.EventNames.Should().NotContain("CrossHostReleaseSignalAbsent");
    }

    [Fact]
    public async Task should_wake_a_waiter_in_this_process_on_release_when_no_transport_is_configured()
    {
        // given - a frozen clock, so the waiter's backoff poll never fires and only the release can wake it
        using var waiting = new EventSignalLoggerProvider("DelayBeforeRetry");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(waiting));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(10);
        var holder = await locks.TryAcquireAsync(resource, cancellationToken: AbortToken);
        holder.Should().NotBeNull();
        var waiter = locks.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(1) },
            AbortToken
        );

        // The waiter logs its backoff only after it registers for wake-ups, so a release from here on reaches it.
        await waiting.Logged.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        await locks.ReleaseAsync(resource, holder!.LeaseId, AbortToken);
        var acquired = await waiter.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        acquired.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_no_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        var act = () => services.AddHeadlessDistributedLocks(_ => { });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseInMemory*UseRedis*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public void should_throw_when_multiple_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        var act = () =>
            services.AddHeadlessDistributedLocks(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemory();
            });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_throw_when_add_headless_distributed_locks_is_called_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        // when
        var act = () => services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_default_the_storage_schema_to_the_shared_headless_schema()
    {
        new DistributedLocksStorageOptions().Schema.Should().Be("headless");
    }

    /// <summary>Completes <see cref="Logged"/> the first time any logger writes the named event.</summary>
    private sealed class EventSignalLoggerProvider(string eventName) : ILoggerProvider
    {
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Logged => _logged.Task;

        public ILogger CreateLogger(string categoryName) => new EventSignalLogger(eventName, _logged);

        public void Dispose() { }

        private sealed class EventSignalLogger(string eventName, TaskCompletionSource logged) : ILogger
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
                if (string.Equals(eventId.Name, eventName, StringComparison.Ordinal))
                {
                    logged.TrySetResult();
                }
            }
        }
    }

    /// <summary>Records only the event names the lock primitives log, which is all these tests assert on.</summary>
    private sealed class EventNameLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentBag<string> _eventNames = [];

        public IReadOnlyCollection<string> EventNames => _eventNames;

        public ILogger CreateLogger(string categoryName) => new EventNameLogger(_eventNames);

        public void Dispose() { }

        private sealed class EventNameLogger(ConcurrentBag<string> eventNames) : ILogger
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
                if (eventId.Name is { } name)
                {
                    eventNames.Add(name);
                }
            }
        }
    }
}
