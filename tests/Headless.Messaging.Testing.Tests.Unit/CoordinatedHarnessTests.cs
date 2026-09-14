// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ─── Message types ────────────────────────────────────────────────────────────

public sealed record CoordinatedOrderPlaced(string Id);

public sealed class CoordinatedOrderPlacedConsumer : IConsume<CoordinatedOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<CoordinatedOrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed record StandaloneOrderPlaced(string Id);

public sealed class StandaloneOrderPlacedConsumer : IConsume<StandaloneOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<StandaloneOrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="MessagingTestHarness.RunCoordinatedAsync(Func{Task})"/> opens a non-relational commit scope so a test
/// can exercise <see cref="DeliveryMode.Coordinated"/>: a commit stores and dispatches the captured rows, a rollback
/// discards them, and a coordinated type published outside any scope is rejected before storage or transport.
/// In-memory storage offers only the ProcessLocal inbox tier, and the startup gate refuses a Coordinated registration
/// beside durable consumers below Transactional, so the Coordinated type lives in a publish-only host and consumers
/// are exercised through the scope with a default (Durable) type, which enlists in the same scope.
/// </summary>
public sealed class CoordinatedHarnessTests : TestBase
{
    private static Task<MessagingTestHarness> _CreatePublishOnlyHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Bus.ForMessage<CoordinatedOrderPlaced>(message =>
                    message.Contract("coordinated-order-placed").WithDeliveryMode(DeliveryMode.Coordinated)
                );
            });
        });
    }

    private static Task<MessagingTestHarness> _CreateConsumerHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                setup.Bus.ForMessage<StandaloneOrderPlaced>(message =>
                    message
                        .Contract("standalone-order-placed")
                        .Consumer<StandaloneOrderPlacedConsumer>(consumer =>
                            consumer.ConsumerIdentity("tests.messaging-testing.standalone-order-placed")
                        )
                );
            });
        });
    }

    [Fact]
    public async Task should_record_coordinated_message_when_scope_commits()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        await harness.RunCoordinatedAsync(() =>
            harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C1"), cancellationToken: AbortToken)
        );

        var published = await harness.WaitForPublished<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);

        published.Message.Should().BeOfType<CoordinatedOrderPlaced>().Which.Id.Should().Be("C1");
        published.RequestedDeliveryMode.Should().Be(DeliveryMode.Coordinated);
        published.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        harness.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task should_record_nothing_when_scope_rolls_back()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var act = () =>
            harness.RunCoordinatedAsync(async () =>
            {
                await harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C2"), cancellationToken: AbortToken);
                throw new InvalidOperationException("boom");
            });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        // A later committed publish flows through the same single-threaded sender: had the rolled-back row been
        // handed to the dispatcher it would have been recorded before this one.
        await harness.RunCoordinatedAsync(() =>
            harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C3"), cancellationToken: AbortToken)
        );
        await harness.WaitForPublished<CoordinatedOrderPlaced>(
            m => string.Equals(m.Id, "C3", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        harness
            .Published.Should()
            .ContainSingle()
            .Which.Message.Should()
            .BeOfType<CoordinatedOrderPlaced>()
            .Which.Id.Should()
            .Be("C3");

        var monitoring = harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var page = await monitoring.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Publish,
                CurrentPage = 0,
                PageSize = 10,
            },
            AbortToken
        );
        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task should_return_delegate_result_after_commit()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var result = await harness.RunCoordinatedAsync(async () =>
        {
            await harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C4"), cancellationToken: AbortToken);
            return 42;
        });

        result.Should().Be(42);
        await harness.WaitForPublished<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_reject_coordinated_type_published_outside_scope()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var act = () => harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C5"), cancellationToken: AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Coordinated delivery requires a compatible live commit-coordination scope*");
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_coordinated_type_beside_consumers_on_process_local_inbox()
    {
        // Pins the topology constraint the harness documents: in-memory storage cannot offer the Transactional inbox
        // tier, so a Coordinated registration next to a durable consumer fails at bootstrap, not on every publish.
        var act = () =>
            MessagingTestHarness.CreateAsync(services =>
            {
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                    setup.Bus.ForMessage<CoordinatedOrderPlaced>(message =>
                        message
                            .Contract("coordinated-order-placed")
                            .WithDeliveryMode(DeliveryMode.Coordinated)
                            .Consumer<CoordinatedOrderPlacedConsumer>(consumer =>
                                consumer.ConsumerIdentity("tests.messaging-testing.coordinated-order-placed")
                            )
                    );
                });
            });

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage(
                "*Coordinated delivery with durable consumers requires RequiredInboxCapability = Transactional*"
            );
    }

    [Fact]
    public async Task should_enlist_default_publish_in_scope_and_consume_after_commit()
    {
        await using var harness = await _CreateConsumerHarnessAsync();

        // A Durable (default) publish inside a compatible live scope is captured on that scope too, so a rollback
        // discards it exactly like a Coordinated one.
        var act = () =>
            harness.RunCoordinatedAsync(async () =>
            {
                await harness.Publisher.PublishAsync(new StandaloneOrderPlaced("S1"), cancellationToken: AbortToken);
                throw new InvalidOperationException("boom");
            });
        await act.Should().ThrowAsync<InvalidOperationException>();

        await harness.RunCoordinatedAsync(() =>
            harness.Publisher.PublishAsync(new StandaloneOrderPlaced("S2"), cancellationToken: AbortToken)
        );
        var consumed = await harness.WaitForConsumed<StandaloneOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);

        consumed.Message.Should().BeOfType<StandaloneOrderPlaced>().Which.Id.Should().Be("S2");
        consumed.RequestedDeliveryMode.Should().Be(DeliveryMode.Durable);
        consumed.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
    }
}
