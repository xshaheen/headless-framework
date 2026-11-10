// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Module;

/// <summary>The Messaging command rules that depend on how the node is deployed or which store holds a row.</summary>
public sealed class MessagingCommandRuleTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly MessagingMetricsEventListener _metrics = new();

    protected override async ValueTask DisposeAsyncCore()
    {
        _metrics.Dispose();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_disable_circuit_and_node_commands_when_discovery_is_kubernetes()
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var services = _Services(kubernetes: true);
        var module = _Module(_Stores(primary), services);

        // when
        var answers = await module.GetAllowedCommandsAsync(
            new DashboardAllowedCommandsRequest
            {
                Targets =
                [
                    MessagingViews.Ref(
                        MessagingViews.CircuitKind,
                        MessagingViews.CircuitId("node-a", MessagingRows.Consumer)
                    ),
                    MessagingViews.Ref(MessagingViews.NodeKind, "node-a"),
                ],
                User = _Operator(),
            },
            AbortToken
        );

        // then
        answers.Should().HaveCount(3);
        answers.Should().OnlyContain(answer => answer.DisabledReason == MessagingCommands.OnePodOnly);
    }

    [Fact]
    public async Task should_allow_reset_circuit_when_discovery_names_the_current_node()
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var services = _Services(kubernetes: false);
        var module = _Module(_Stores(primary), services);

        // when
        var answers = await module.GetAllowedCommandsAsync(
            new DashboardAllowedCommandsRequest
            {
                Targets =
                [
                    MessagingViews.Ref(
                        MessagingViews.CircuitKind,
                        MessagingViews.CircuitId("node-a", MessagingRows.Consumer)
                    ),
                ],
                User = _Operator(),
            },
            AbortToken
        );

        // then
        answers
            .Single(answer =>
                string.Equals(answer.CommandId, MessagingCommands.ResetCircuitId, StringComparison.Ordinal)
            )
            .DisabledReason.Should()
            .BeNull();
        answers
            .Single(answer =>
                string.Equals(answer.CommandId, MessagingCommands.ForceOpenCircuitId, StringComparison.Ordinal)
            )
            .DisabledReason.Should()
            .Be(MessagingCommands.AlreadyOpen);
    }

    [Fact]
    public async Task should_disable_scheduled_commands_when_the_row_is_in_an_additional_outbox()
    {
        // given
        await using var main = _MainStore(out var primary);
        var storageId = Guid.NewGuid();
        var outbox = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        var row = MessagingRows.Medium("invoice-1");
        row.StorageId = storageId;
        row.StatusName = StatusName.Delayed;
        monitoring.GetPublishedMessageAsync(storageId, Arg.Any<CancellationToken>()).Returns(row);
        outbox.GetMonitoringApi().Returns(monitoring);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, outbox), services);
        var id = $"node-a~billing~{storageId:D}";

        // when
        var answers = await module.GetAllowedCommandsAsync(
            new DashboardAllowedCommandsRequest
            {
                Targets = [MessagingViews.Ref(MessagingViews.PublishedKind, id)],
                User = _Operator(),
            },
            AbortToken
        );
        var dispatch = await module.ExecuteAsync(_Request(MessagingCommands.DispatchNowId, id), AbortToken);

        // then
        answers
            .Where(answer => answer.CommandId is MessagingCommands.DispatchNowId or MessagingCommands.RevokeId)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(answer => answer.DisabledReason == MessagingCommands.NotSupportedForOutboxes);
        dispatch.Refusal.Should().Be(MessagingCommands.NotSupportedForOutboxes);
        outbox.DidNotReceive().GetScheduledDeliveryOperationsApi();
    }

    [Fact]
    public async Task should_report_the_failure_on_the_row_when_the_re_send_receipt_write_fails()
    {
        // given
        await using var main = _MainStore(out var real);
        var row = await MessagingRows.PublishedAsync(real, "invoice-1", StatusName.Failed, AbortToken);
        var primary = Substitute.For<IDataStorage>();
        primary.GetMonitoringApi().Returns(real.GetMonitoringApi());
        var published = Substitute.For<IPublishedOperationsApi>();
        published
            .ResendAsync(Arg.Any<PublishedOperationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The receipt could not be written."));
        primary.GetPublishedOperationsApi().Returns(published);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary), services);
        var id = $"node-a~primary~{row.StorageId:D}";

        // when
        var result = await module.ExecuteAsync(_Request(MessagingCommands.ResendId, id), AbortToken);

        // then
        result.Refusal.Should().BeNull();
        var failed = result.Rows.Should().ContainSingle().Subject;
        failed.Outcome.Should().Be(DashboardRowOutcome.Failed);
        failed.Reason.Should().Be("The receipt could not be written.");
    }

    [Theory]
    [InlineData(MessagingCommands.ResendId, MessagingOperationType.Resend)]
    [InlineData(MessagingCommands.DeleteId, MessagingOperationType.Purge)]
    public async Task should_change_the_row_on_the_outbox_and_record_its_receipt_in_the_main_store_when_the_row_is_in_an_additional_outbox(
        string commandId,
        MessagingOperationType expectedType
    )
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var billing = _MainStore(out var backing);
        var row = await MessagingRows.PublishedAsync(backing, "invoice-1", StatusName.Failed, AbortToken);
        var outbox = _Outbox(backing);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, outbox), services);
        var id = $"node-a~billing~{row.StorageId:D}";

        // when
        var result = await module.ExecuteAsync(_Request(commandId, id), AbortToken);

        // then
        result.Refusal.Should().BeNull();
        var done = result.Rows.Should().ContainSingle().Subject;
        done.Outcome.Should().Be(DashboardRowOutcome.Done);
        done.Reason.Should().BeNull();
        await ((IAdditionalOutboxOperations)outbox)
            .Received(1)
            .ApplyPublishedWithoutReceiptAsync(
                expectedType,
                Arg.Is<PublishedOperationRequest>(write => write.StorageId == row.StorageId),
                Arg.Any<CancellationToken>()
            );
        var receipt = (await _PublishedReceiptsAsync(primary)).Should().ContainSingle().Subject;
        receipt.OperationType.Should().Be(expectedType);
        receipt.Outcome.Should().Be(InboxOperationOutcome.Applied);
        receipt.StorageId.Should().Be(row.StorageId);
        receipt.Actor.Should().Be("ops");
        receipt.Reason.Should().Be("operator check");
        receipt.OutboxName.Should().Be("billing");
    }

    [Fact]
    public async Task should_keep_the_row_done_and_note_the_missing_receipt_when_the_main_store_receipt_write_fails_for_an_outbox_row()
    {
        // given
        await using var main = _MainStore(out var real);
        await using var billing = _MainStore(out var backing);
        var row = await MessagingRows.PublishedAsync(backing, "invoice-1", StatusName.Failed, AbortToken);
        var primary = Substitute.For<IDataStorage, IAdditionalOutboxOperations>();
        primary.GetMonitoringApi().Returns(real.GetMonitoringApi());
        ((IAdditionalOutboxOperations)primary)
            .RecordOutboxReceiptAsync(
                Arg.Any<string>(),
                Arg.Any<PublishedOperationResult>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("The ledger is unreachable."));
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, _Outbox(backing)), services);
        var id = $"node-a~billing~{row.StorageId:D}";

        // when
        var result = await module.ExecuteAsync(_Request(MessagingCommands.ResendId, id), AbortToken);

        // then: the outbox already re-sent the row, so the change stands and the gap is reported beside it
        var done = result.Rows.Should().ContainSingle().Subject;
        done.Outcome.Should().Be(DashboardRowOutcome.Done);
        done.Reason.Should().Be("The receipt was not recorded: The ledger is unreachable.");
        var resent = await backing.GetMonitoringApi().GetPublishedMessageAsync(row.StorageId, AbortToken);
        resent!.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task should_skip_the_row_as_not_supported_when_the_outbox_storage_cannot_write_without_a_receipt()
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var billing = _MainStore(out var backing);
        var row = await MessagingRows.PublishedAsync(backing, "invoice-1", StatusName.Failed, AbortToken);
        var outbox = Substitute.For<IDataStorage>();
        outbox.GetMonitoringApi().Returns(backing.GetMonitoringApi());
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, outbox), services);
        var id = $"node-a~billing~{row.StorageId:D}";

        // when
        var result = await module.ExecuteAsync(_Request(MessagingCommands.ResendId, id), AbortToken);

        // then
        result.Refusal.Should().Be(MessagingCommands.NotSupportedForOutboxes);
        var skipped = result.Rows.Should().ContainSingle().Subject;
        skipped.Outcome.Should().Be(DashboardRowOutcome.Skipped);
        skipped.Reason.Should().Be(MessagingCommands.NotSupportedForOutboxes);
        (await _PublishedReceiptsAsync(primary)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_a_re_send_operation_over_the_outbox_with_receipts_in_the_main_store_when_the_filter_names_the_outbox()
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var billing = _MainStore(out var backing);
        var first = await MessagingRows.PublishedAsync(backing, "invoice-1", StatusName.Failed, AbortToken);
        var second = await MessagingRows.PublishedAsync(backing, "invoice-2", StatusName.Failed, AbortToken);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, _Outbox(backing)), services);
        using var filter = JsonDocument.Parse("""{ "store": "billing", "status": "Failed" }""");

        // when
        var scope = await module.PrepareAsync(
            new DashboardOperationScopeRequest
            {
                CommandId = MessagingCommands.ResendId,
                Filter = filter.RootElement,
                CountLimit = 100,
                SampleSize = 5,
                User = _Operator(),
            },
            AbortToken
        );
        var chunk = await module.RunChunkAsync(
            new DashboardOperationChunkRequest
            {
                CommandId = MessagingCommands.ResendId,
                Filter = filter.RootElement,
                HighWaterMark = scope.HighWaterMark,
                ChunkSize = 500,
                Reason = "billing broker recovered",
                User = _Operator(),
            },
            AbortToken
        );

        // then
        scope.Count.Should().Be(2);
        chunk.NextCursor.Should().BeNull();
        chunk.Rows.Should().HaveCount(2).And.OnlyContain(row => row.Outcome == DashboardRowOutcome.Done);
        chunk
            .Rows.Select(row => row.Id)
            .Should()
            .BeEquivalentTo($"node-a~billing~{first.StorageId:D}", $"node-a~billing~{second.StorageId:D}");
        (await _PublishedReceiptsAsync(primary))
            .Should()
            .OnlyContain(static receipt => receipt.OutboxName == "billing")
            .And.Subject.Select(receipt => receipt.StorageId)
            .Should()
            .BeEquivalentTo([first.StorageId, second.StorageId]);
    }

    [Fact]
    public async Task should_refuse_a_received_row_operation_when_the_filter_names_an_additional_outbox()
    {
        // given
        await using var main = _MainStore(out var primary);
        await using var billing = _MainStore(out var backing);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var module = _Module(_Stores(primary, _Outbox(backing)), services);
        using var filter = JsonDocument.Parse("""{ "store": "billing" }""");

        // when
        var prepare = async () =>
            await module.PrepareAsync(
                new DashboardOperationScopeRequest
                {
                    CommandId = MessagingCommands.ReExecuteId,
                    Filter = filter.RootElement,
                    CountLimit = 100,
                    SampleSize = 5,
                    User = _Operator(),
                },
                AbortToken
            );

        // then
        await prepare.Should().ThrowAsync<ArgumentException>().WithMessage("Store 'billing' is an additional outbox*");
    }

    // A second in-memory store stands in for the outbox's database: its own API performs the write, while the ledger
    // the tests read is the main store's.
    private static IDataStorage _Outbox(IDataStorage backing)
    {
        var outbox = Substitute.For<IDataStorage, IAdditionalOutboxOperations>();
        var published = backing.GetPublishedOperationsApi();
        outbox.GetMonitoringApi().Returns(backing.GetMonitoringApi());
        outbox.GetPublishedOperationsApi().Returns(published);
        outbox
            .GetLatestMessageChangeMarkerAsync(Arg.Any<CancellationToken>())
            .Returns(call => backing.GetLatestMessageChangeMarkerAsync(call.Arg<CancellationToken>()));
        ((IAdditionalOutboxOperations)outbox)
            .ApplyPublishedWithoutReceiptAsync(
                Arg.Any<MessagingOperationType>(),
                Arg.Any<PublishedOperationRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.Arg<MessagingOperationType>() == MessagingOperationType.Resend
                    ? published.ResendAsync(call.Arg<PublishedOperationRequest>(), call.Arg<CancellationToken>())
                    : published.PurgeAsync(call.Arg<PublishedOperationRequest>(), call.Arg<CancellationToken>())
            );

        return outbox;
    }

    private static async Task<IReadOnlyList<MessagingOperationReceiptView>> _PublishedReceiptsAsync(
        IDataStorage storage
    )
    {
        var page = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery { TargetKind = MessagingOperationTargetKind.Published },
                new OperatorAuthorizationContext(_Operator()),
                AbortToken
            );

        return page.Items;
    }

    private ServiceProvider _MainStore(out IDataStorage storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        var provider = services.BuildServiceProvider();
        storage = provider.GetRequiredService<IDataStorage>();

        return provider;
    }

    // Kubernetes discovery registers no current-node identity; every other discovery provider registers one.
    private static ServiceProvider _Services(bool kubernetes)
    {
        var monitor = Substitute.For<ICircuitBreakerMonitor>();
        monitor.GetSnapshot(MessagingRows.Consumer).Returns(MessagingCommandHost.Snapshot(CircuitBreakerState.Open));
        var services = new ServiceCollection()
            .AddSingleton(monitor)
            .AddSingleton(Substitute.For<IRetryProcessorMonitor>())
            .AddSingleton(Substitute.For<INodeDiscoveryProvider>());

        if (!kubernetes)
        {
            services.AddSingleton(new CurrentNodeIdentity("node-a"));
        }

        return services.BuildServiceProvider();
    }

    private static MessagingStores _Stores(IDataStorage primary, IDataStorage? outbox = null)
    {
        MessagingStore[] stores = outbox is null
            ? [new("primary", primary, HoldsReceived: true)]
            : [new("primary", primary, HoldsReceived: true), new("billing", outbox, HoldsReceived: false)];

        return new MessagingStores("node-a", stores, TimeSpan.FromSeconds(5));
    }

    private MessagingModule _Module(MessagingStores stores, IServiceProvider services) =>
        new(stores, _metrics, new MessagingDashboardOptions { NodeName = "node-a" }, services);

    private static DashboardCommandRequest _Request(string commandId, string id) =>
        new()
        {
            CommandId = commandId,
            Ids = [id],
            Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            Reason = "operator check",
            Channel = DashboardChannels.Ui,
            User = _Operator(),
        };

    private static ClaimsPrincipal _Operator() => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ops")], "test"));
}
