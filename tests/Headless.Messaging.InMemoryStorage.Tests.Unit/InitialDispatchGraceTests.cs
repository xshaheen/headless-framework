// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Pins the <c>InitialDispatchGrace</c> exclusion contract for the in-memory data storage.
/// </summary>
/// <remarks>
/// PR #254 review finding #9. All three storage providers (InMemory, PostgreSQL, SQL Server)
/// set <c>NextRetryAt = added + InitialDispatchGrace</c> on initial store and rely on the
/// pickup query's <c>NextRetryAt &lt;= now</c> predicate to skip freshly-stored messages
/// during the grace window. A regression that flips the sign or omits the offset would
/// silently turn the retry processor into a racer of the normal dispatch path. This test
/// fails fast when that happens for InMemory; PostgreSQL and SQL Server need an equivalent
/// integration test once <see cref="DataStorageTestsBase"/> exposes a <see cref="TimeProvider"/>
/// seam (tracked separately).
/// </remarks>
public sealed class InitialDispatchGraceTests
{
    private static readonly DateTimeOffset _FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _InitialDispatchGrace = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_exclude_freshly_stored_published_message_during_initial_dispatch_grace()
    {
        var (storage, fakeClock) = _BuildStorageWithFakeClock();
        await new InMemoryStorageInitializer().InitializeAsync(TestContext.Current.CancellationToken);

        // given — store a fresh published message; NextRetryAt is now + 30s.
        var stored = await storage.StoreMessageAsync(
            "grace-published",
            _CreateMessage(),
            cancellationToken: TestContext.Current.CancellationToken
        );

        // when — immediately poll (still inside the grace window).
        var beforeGrace = (
            await storage.GetPublishedMessagesOfNeedRetryAsync(TestContext.Current.CancellationToken)
        ).ToList();

        // then — pickup query excludes the row.
        beforeGrace.Should().NotContain(m => m.StorageId == stored.StorageId);

        // when — advance the clock past the grace window.
        fakeClock.Advance(_InitialDispatchGrace + TimeSpan.FromSeconds(1));

        // then — the same row is now eligible for pickup.
        var afterGrace = (
            await storage.GetPublishedMessagesOfNeedRetryAsync(TestContext.Current.CancellationToken)
        ).ToList();
        afterGrace.Should().Contain(m => m.StorageId == stored.StorageId);
    }

    [Fact]
    public async Task should_exclude_freshly_stored_received_message_during_initial_dispatch_grace()
    {
        var (storage, fakeClock) = _BuildStorageWithFakeClock();
        await new InMemoryStorageInitializer().InitializeAsync(TestContext.Current.CancellationToken);

        // given — store a fresh received message; NextRetryAt is now + 30s.
        var stored = await storage.StoreReceivedMessageAsync(
            "grace-received",
            "test-group",
            _CreateMessage(),
            TestContext.Current.CancellationToken
        );

        // when — immediately poll (still inside the grace window).
        var beforeGrace = (
            await storage.GetReceivedMessagesOfNeedRetryAsync(TestContext.Current.CancellationToken)
        ).ToList();

        // then — pickup query excludes the row.
        beforeGrace.Should().NotContain(m => m.StorageId == stored.StorageId);

        // when — advance the clock past the grace window.
        fakeClock.Advance(_InitialDispatchGrace + TimeSpan.FromSeconds(1));

        // then — the same row is now eligible for pickup.
        var afterGrace = (
            await storage.GetReceivedMessagesOfNeedRetryAsync(TestContext.Current.CancellationToken)
        ).ToList();
        afterGrace.Should().Contain(m => m.StorageId == stored.StorageId);
    }

    private static (InMemoryDataStorage Storage, FakeTimeProvider Clock) _BuildStorageWithFakeClock()
    {
        var fakeClock = new FakeTimeProvider(_FixedNow);

        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.RetryPolicy.InitialDispatchGrace = _InitialDispatchGrace;
            x.RetryPolicy.MaxPersistedRetries = 4;
        });
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton<TimeProvider>(fakeClock);

        var provider = services.BuildServiceProvider();
        var storage = new InMemoryDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<ISerializer>(),
            provider.GetRequiredService<ILongIdGenerator>(),
            fakeClock
        );

        return (storage, fakeClock);
    }

    private static Message _CreateMessage() =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cap-msg-id"] = Guid.NewGuid().ToString("N"),
                ["cap-msg-name"] = "test-message",
            },
            value: null
        );
}
