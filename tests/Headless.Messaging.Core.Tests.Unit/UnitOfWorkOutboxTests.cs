// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// <c>unit.Outbox</c> is the enlisted half of the publish split: the row it writes lives inside the unit's
/// transaction, so it appears on completion and is discarded on rollback. The decisive assertions here are the
/// negative ones — a happy-path row is equally present whether the publish enlisted or wrote standalone.
/// </summary>
public sealed class UnitOfWorkOutboxTests : TestBase
{
    [Fact]
    public async Task should_discard_the_durable_row_when_the_unit_of_work_rolls_back()
    {
        // given
        await using var host = _CreateHost();
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var receipt = await unitOfWork.Outbox.PublishAsync(new Placed("rolled-back"), AbortToken);

        // then — captured on the unit, not written standalone: nothing is visible before the unit resolves
        receipt.StorageId.Should().NotBeNull();
        (await host.Monitoring.GetPublishedMessageAsync(receipt.StorageId!.Value, AbortToken)).Should().BeNull();

        // when
        await unitOfWork.RollbackAsync();

        // then — the row never lands and nothing is handed to the dispatcher
        (await host.Monitoring.GetPublishedMessageAsync(receipt.StorageId.Value, AbortToken))
            .Should()
            .BeNull();
        (await _CountPublishedAsync(host)).Should().Be(0);
        host.Dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_one_durable_row_and_dispatch_it_when_the_unit_of_work_completes()
    {
        // given
        await using var host = _CreateHost();
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);
        var receipt = await unitOfWork.Outbox.PublishAsync(new Placed("committed"), AbortToken);
        host.Dispatcher.CommittedMessages.Should().BeEmpty("dispatch waits for the commit edge");

        // when
        await unitOfWork.CompleteAsync(AbortToken);

        // then
        (await _CountPublishedAsync(host))
            .Should()
            .Be(1);
        (await host.Monitoring.GetPublishedMessageAsync(receipt.StorageId!.Value, AbortToken)).Should().NotBeNull();
        host.Dispatcher.CommittedMessages.Should().ContainSingle().Which.StorageId.Should().Be(receipt.StorageId.Value);
    }

    [Fact]
    public async Task should_throw_before_writing_anything_when_the_storage_cannot_join_the_unit_of_work()
    {
        // given — a relational resource the in-memory storage cannot be atomic with
        await using var host = _CreateHost();
        await using var scope = host.Provider.CreateAsyncScope();
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().Enlist(resource);

        // when
        var act = () => unitOfWork.Outbox.PublishAsync(new Placed("unjoinable"), AbortToken);

        // then — refused rather than degraded to a standalone row
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("cannot join the active unit of work");
        (await _CountPublishedAsync(host)).Should().Be(0);
        host.Dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_no_unit_of_work_can_be_joined_at_all()
    {
        // given — a storage that contributes no coordination resolver can join nothing, so a resource-less unit
        // reads as no unit at all. This is the refusal the coordinated path owes a caller who asked to enlist.
        await using var host = _CreateHost(removeCoordinationResolver: true);
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var act = () => unitOfWork.Outbox.PublishAsync(new Placed("unjoinable"), AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("requires an active unit of work");
        (await _CountPublishedAsync(host)).Should().Be(0);
        host.Dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_publishing_on_a_unit_that_already_completed()
    {
        // given — a completed unit: nothing can enlist in it any more
        await using var host = _CreateHost();
        var factory = host.Provider.GetRequiredService<IUnitOfWorkFactory>();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);

        // when
        var act = () => unitOfWork.Outbox.PublishAsync(new Placed("dead-unit"), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _CountPublishedAsync(host)).Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_a_binding_taken_before_the_unit_completed_is_used_after_it()
    {
        // given — the binding is taken while the unit is live, then retained past its completion
        await using var host = _CreateHost();
        var factory = host.Provider.GetRequiredService<IUnitOfWorkFactory>();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);
        var outbox = unitOfWork.Outbox;
        await unitOfWork.CompleteAsync(AbortToken);

        // when
        var act = () => outbox.PublishAsync(new Placed("stale-binding"), AbortToken);

        // then — liveness is a per-publish check, not a per-accessor one
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _CountPublishedAsync(host)).Should().Be(0);
    }

    [Fact]
    public async Task should_keep_two_units_outbox_work_independent()
    {
        // given — two units from one factory share nothing: each holds its own buffered rows
        await using var host = _CreateHost();
        var factory = host.Provider.GetRequiredService<IUnitOfWorkFactory>();
        var first = await factory.BeginAsync(cancellationToken: AbortToken);
        var second = await factory.BeginAsync(cancellationToken: AbortToken);

        // when
        await first.Outbox.PublishAsync(new Placed("first"), AbortToken);
        await second.Outbox.PublishAsync(new Placed("second"), AbortToken);
        await second.RollbackAsync();

        // then — the rolled-back unit's row is discarded; the other unit is untouched and still commits its own
        (await _CountPublishedAsync(host))
            .Should()
            .Be(0);

        // when
        await first.CompleteAsync(AbortToken);

        // then
        (await _CountPublishedAsync(host))
            .Should()
            .Be(1);
        host.Dispatcher.CommittedMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task should_route_each_verb_to_its_own_lane()
    {
        // given
        await using var host = _CreateHost();
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var published = await unitOfWork.Outbox.PublishAsync(new Placed("bus"), AbortToken);
        var enqueued = await unitOfWork.Outbox.EnqueueAsync(new Placed("queue"), AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);

        // then
        var busRow = await host.Monitoring.GetPublishedMessageAsync(published.StorageId!.Value, AbortToken);
        var queueRow = await host.Monitoring.GetPublishedMessageAsync(enqueued.StorageId!.Value, AbortToken);
        busRow!.Lane.Should().Be(MessageLane.Bus);
        queueRow!.Lane.Should().Be(MessageLane.Queue);
    }

    [Fact]
    public async Task should_name_the_registration_call_when_no_messaging_is_registered()
    {
        // given — a host with a unit of work but no messaging
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUnitOfWork();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await using var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var act = () => unitOfWork.Outbox;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessMessaging*");
    }

    private static async Task<int> _CountPublishedAsync(OutboxHost host)
    {
        var page = await host.Monitoring.GetMessagesAsync(
            new MessageQuery { MessageType = MessageType.Publish, PageSize = 50 },
            AbortToken
        );

        return page.TotalItems;
    }

    private static OutboxHost _CreateHost(bool removeCoordinationResolver = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
#pragma warning disable CA2000 // Ownership passes to the container on the AddSingleton below; it disposes the instance with the provider.
        var dispatcher = new RecordingCommittedDispatcher();
#pragma warning restore CA2000
        // Registered before AddHeadlessMessaging so it wins the TryAddSingleton: the committed-dispatch hand-off
        // is what distinguishes "buffered on the unit" from "written and dispatched immediately".
        services.AddSingleton<IDispatcher>(dispatcher);
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<Placed>(message => message.Contract("tests.unit-of-work-outbox.placed"));
            setup.Queue.ForMessage<Placed>(message => message.Contract("tests.unit-of-work-outbox.placed"));
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        if (removeCoordinationResolver)
        {
            services.RemoveAll<IDeliveryCoordinationResolver>();
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        return new OutboxHost(provider, dispatcher);
    }

    private sealed record Placed(string Value);

    private sealed class OutboxHost(ServiceProvider provider, RecordingCommittedDispatcher dispatcher)
        : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;

        public RecordingCommittedDispatcher Dispatcher => dispatcher;

        public IMonitoringApi Monitoring { get; } = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await dispatcher.DisposeAsync();
        }
    }

    private sealed class RecordingCommittedDispatcher : IDispatcher, ICommittedMessageDispatcher
    {
        public List<MediumMessage> CommittedMessages { get; } = [];

        public void EnqueueCommittedMessage(MediumMessage message) => CommittedMessages.Add(message);

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
