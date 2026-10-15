// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A message published with <c>DeliverWithin</c> carries its delivery deadline into the stored outbox row. When the
/// relay reaches a row after that deadline, it marks the row terminally failed with the reason and never hands it to
/// the transport; a row reached in time is relayed as usual. Runs the production publisher, the provider's real
/// storage, and the production outbox sender, so each storage proves the terminal write it persists.
/// </summary>
[PublicAPI]
public abstract class DeliverWithinRelayConformanceTests : TestBase
{
    /// <summary>Registers the storage provider under test on the messaging setup.</summary>
    protected abstract void UseStorage(MessagingSetupBuilder setup);

    [Fact]
    public async Task should_mark_expired_outbox_row_failed_without_sending_when_relay_reaches_it_late()
    {
        // given — a durable publish with a one-minute deadline that the relay reaches two minutes later
        var name = $"tests.deliver-within.{Guid.NewGuid():N}";
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var transport = new RecordingBusTransport();
        await using var provider = _BuildProvider(name, clock, transport);
        await provider.ApplyMessagingSchemaAsync(AbortToken);

        var receipt = await provider
            .GetRequiredService<IBus>()
            .PublishAsync(
                new DeliverWithinProbe("late"),
                new PublishOptions { DeliverWithin = TimeSpan.FromMinutes(1) },
                AbortToken
            );
        clock.Advance(TimeSpan.FromMinutes(2));
        var stored = await _ReadStoredAsync(provider, receipt);

        // when
        var result = await provider.GetRequiredService<IMessageSender>().SendAsync(stored);

        // then — terminal, with the reason in the stored envelope, nothing sent, nothing scheduled for retry
        result.Succeeded.Should().BeFalse();
        transport.Sent.Should().BeEmpty();
        var view = await _ReadViewAsync(provider, name);
        view.StatusName.Should().Be(StatusName.Failed);
        view.NextRetryAt.Should().BeNull();
        view.ExpiresAt.Should().NotBeNull("a dropped row keeps the failed-message retention");
        view.Content.Should().Contain("MessageDeliverByExceededException");
        view.Content.Should().Contain(Headers.DeliverBy);
    }

    [Fact]
    public async Task should_relay_outbox_row_when_relay_reaches_it_before_delivery_deadline()
    {
        // given
        var name = $"tests.deliver-within.{Guid.NewGuid():N}";
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var transport = new RecordingBusTransport();
        await using var provider = _BuildProvider(name, clock, transport);
        await provider.ApplyMessagingSchemaAsync(AbortToken);

        var receipt = await provider
            .GetRequiredService<IBus>()
            .PublishAsync(
                new DeliverWithinProbe("on-time"),
                new PublishOptions { DeliverWithin = TimeSpan.FromMinutes(5) },
                AbortToken
            );
        var stored = await _ReadStoredAsync(provider, receipt);

        // when
        var result = await provider.GetRequiredService<IMessageSender>().SendAsync(stored);

        // then — the transport received the deadline with the message, so a broker that can enforce it does
        result.Succeeded.Should().BeTrue();
        var sent = transport.Sent.Should().ContainSingle().Subject;
        sent.Headers.Should().ContainKey(Headers.DeliverBy);
        (await _ReadViewAsync(provider, name)).StatusName.Should().Be(StatusName.Succeeded);
    }

    private ServiceProvider _BuildProvider(string messageName, TimeProvider clock, RecordingBusTransport transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            UseStorage(setup);
        });
        services.ConfigureMessaging(messaging => messaging.Message<DeliverWithinProbe>(messageName));

        // Registered last so the relay resolves it instead of the in-memory transport: the test observes every send.
        services.AddSingleton<IBusTransport>(transport);

        return services.BuildServiceProvider();
    }

    private async Task<MediumMessage> _ReadStoredAsync(IServiceProvider provider, PublishReceipt receipt)
    {
        var monitoring = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var stored = await monitoring.GetPublishedMessageAsync(receipt.StorageId!.Value, AbortToken);
        stored.Should().NotBeNull();

        // The relay works from the envelope as stored, the same way a persisted pickup reads it.
        stored!.Origin = provider.GetRequiredService<IMessageSerializer>().Deserialize(stored.Content)!;
        return stored;
    }

    private async Task<MessageView> _ReadViewAsync(IServiceProvider provider, string messageName)
    {
        var monitoring = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var page = await monitoring.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Publish,
                Name = messageName,
                Lane = MessageLane.Bus,
                PageSize = 10,
            },
            AbortToken
        );

        return page.Items.Should().ContainSingle().Subject;
    }

    public sealed record DeliverWithinProbe(string Value);

    private sealed class RecordingBusTransport : IBusTransport
    {
        public ConcurrentQueue<TransportMessage> Sent { get; } = new();

        public BrokerAddress BrokerAddress => new("recording", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Enqueue(message);
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
