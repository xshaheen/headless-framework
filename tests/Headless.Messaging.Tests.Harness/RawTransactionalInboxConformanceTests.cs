// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Tests.Guarantees;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Drives the real consume executor over a raw ADO.NET storage on the default inbox guarantee, with a handler that
/// writes through the attempt unit's own connection and transaction instead of a DbContext.
/// </summary>
public abstract class RawTransactionalInboxConformanceTests : TestBase
{
    private const string _MessageName = "tests.raw-inbox";
    private const string _OutputName = "tests.raw-inbox.output";

    /// <summary>Registers the raw storage under test.</summary>
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    /// <summary>Creates an unopened connection to the storage's database, used outside any attempt.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>Creates the effects table, with an <c>Id</c> and an integer <c>Entry</c> column, when it is missing.</summary>
    protected abstract string CreateEffectsTableSql { get; }

    /// <summary>Inserts one effect row from the <c>@id</c> and <c>@entry</c> parameters.</summary>
    protected abstract string InsertEffectSql { get; }

    /// <summary>Selects the <c>Entry</c> column of every effect row whose id is <c>@id</c>.</summary>
    protected abstract string SelectEffectEntriesSql { get; }

    /// <summary>Overwrites the attempt id of the received row <c>@id</c> with <c>@attempt</c>.</summary>
    protected abstract string ReplaceAttemptSql(string receivedTable);

    [Theory]
    [InlineData(HandlerOutcome.Return)]
    [InlineData(HandlerOutcome.Throw)]
    [InlineData(HandlerOutcome.LoseFence)]
    [Guarantee("MSG-CONSUME-08")]
    public async Task should_commit_or_rollback_writes_through_the_attempt_unit_with_the_inbox_completion(
        HandlerOutcome outcome
    )
    {
        // given: no immediate retry, so one executor call runs exactly one attempt and a failure leaves the row
        // waiting for its delayed retry
        var state = new ExecutionState { Outcome = outcome, InsertEffectSql = InsertEffectSql };
        await using var provider = await _BuildProviderAsync(
            state,
            setup =>
                setup.DefaultFailurePolicy(policy => policy.Delayed(1, TimeSpan.FromHours(1), TimeSpan.FromHours(1)))
        );
        var storage = provider.GetRequiredService<IDataStorage>();
        var descriptor = _GetDescriptor(provider);
        var admitted = await _AdmitAsync(storage, descriptor, state.Id);
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        if (outcome is HandlerOutcome.LoseFence)
        {
            var receivedTable = provider.GetRequiredService<IStorageTableNames>().GetReceivedTableName();
            state.BeforeHandlerReturns = cancellationToken =>
                _ReplaceAttemptAsync(receivedTable, admitted.Message.StorageId, cancellationToken);
        }

        // when
        await using var dispatchScope = provider.CreateAsyncScope();
        var result = await provider
            .GetRequiredService<ISubscribeExecutor>()
            .ExecuteAsync(admitted.Message, dispatchScope.ServiceProvider, descriptor, AbortToken);

        // then
        var committed = outcome is HandlerOutcome.Return;
        result.Succeeded.Should().Be(committed, "{0}", result.Exception);
        switch (outcome)
        {
            case HandlerOutcome.Throw:
                result.Exception.Should().NotBeNull();
                result.Exception!.ToString().Should().Contain(nameof(HandlerFailureException));
                break;
            case HandlerOutcome.LoseFence:
                result.Exception.Should().BeOfType<StaleInboxAttemptException>();
                break;
        }

        state.Entries.Should().ContainSingle("one executor call without immediate retries enters the handler once");
        var entry = state.Entries[0];
        entry.BoundUnit.Should().BeSameAs(entry.ContextUnit, "the attempt binds its connection to the attempt unit");
        (await _ReadEffectEntriesAsync(state.Id)).Should().Equal(committed ? [1] : []);
        await _AssertOutputRowsAsync(storage, state.Id, committed ? 1 : 0);
        (await _AdmitAsync(storage, descriptor, state.Id))
            .Disposition.Should()
            .Be(committed ? InboxAdmissionDisposition.SucceededDuplicate : InboxAdmissionDisposition.InFlightDuplicate);
    }

    [Fact]
    [Guarantee("MSG-CONSUME-08")]
    public async Task should_discard_a_failed_attempt_and_commit_the_retry_in_its_own_attempt()
    {
        // given: the default failure policy retries immediately, and the handler fails only on its first entry
        var state = new ExecutionState
        {
            Outcome = HandlerOutcome.Throw,
            ThrowOnlyOnFirstEntry = true,
            InsertEffectSql = InsertEffectSql,
        };
        await using var provider = await _BuildProviderAsync(state, configure: null);
        var storage = provider.GetRequiredService<IDataStorage>();
        var descriptor = _GetDescriptor(provider);
        var admitted = await _AdmitAsync(storage, descriptor, state.Id);
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);

        // when
        await using var dispatchScope = provider.CreateAsyncScope();
        var result = await provider
            .GetRequiredService<ISubscribeExecutor>()
            .ExecuteAsync(admitted.Message, dispatchScope.ServiceProvider, descriptor, AbortToken);

        // then: the retry ran in a new unit, and only its writes survived
        result.Succeeded.Should().BeTrue("{0}", result.Exception);
        state.Entries.Should().HaveCount(2);
        state.Entries[1].ContextUnit.Should().NotBeSameAs(state.Entries[0].ContextUnit);
        (await _ReadEffectEntriesAsync(state.Id)).Should().Equal(2);
        await _AssertOutputRowsAsync(storage, state.Id, 1);
        (await _AdmitAsync(storage, descriptor, state.Id))
            .Disposition.Should()
            .Be(InboxAdmissionDisposition.SucceededDuplicate);
    }

    private async Task<ServiceProvider> _BuildProviderAsync(
        ExecutionState state,
        Action<MessagingSetupBuilder>? configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            ConfigureStorage(setup);
            configure?.Invoke(setup);
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawInboxMessage>(_MessageName);
            messaging.Message<RawInboxOutput>(_OutputName);
            messaging.AddModule<RawInboxModule>();
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateEffectsTableSql;
        await command.ExecuteNonQueryAsync(AbortToken);

        return provider;
    }

    private static ConsumerExecutorDescriptor _GetDescriptor(IServiceProvider provider) =>
        provider
            .GetRequiredService<MethodMatcherCache>()
            .GetCandidatesBySubscriptionName()
            .Values.SelectMany(descriptors => descriptors)
            .Single();

    private static ValueTask<InboxAdmissionResult> _AdmitAsync(
        IDataStorage storage,
        ConsumerExecutorDescriptor descriptor,
        Guid id
    )
    {
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = descriptor.MessageName,
                [Headers.ConsumerIdentity] = descriptor.ConsumerIdentity,
            },
            new RawInboxMessage(id)
        );

        return storage.AdmitReceivedMessageAsync(
            descriptor.MessageName,
            descriptor.ConsumerIdentity!,
            descriptor.MessageContractVersion!,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken: AbortToken
        );
    }

    // A separate committed connection replaces the persisted fence after the handler's writes, so completing with
    // the original attempt must fail the provider's compare-and-set.
    private async Task _ReplaceAttemptAsync(string receivedTable, Guid storageId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ReplaceAttemptSql(receivedTable);
        _AddParameter(command, "@id", storageId);
        _AddParameter(command, "@attempt", Guid.NewGuid());
        (await command.ExecuteNonQueryAsync(cancellationToken)).Should().Be(1);
    }

    private async Task<List<int>> _ReadEffectEntriesAsync(Guid id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectEffectEntriesSql;
        _AddParameter(command, "@id", id);
        var entries = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        while (await reader.ReadAsync(AbortToken))
        {
            entries.Add(reader.GetInt32(0));
        }

        return entries;
    }

    private static async Task _AssertOutputRowsAsync(IDataStorage storage, Guid id, int expected)
    {
        var monitoring = storage.GetMonitoringApi();
        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            var outgoing = await monitoring.GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = _OutputName,
                    Content = id.ToString(),
                    Lane = lane,
                    PageSize = 10,
                },
                AbortToken
            );
            outgoing.Items.Should().HaveCount(expected, "the {0} outbox row shares the attempt's fate", lane);
        }
    }

    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public enum HandlerOutcome
    {
        Return,
        Throw,
        LoseFence,
    }

    public sealed record RawInboxMessage(Guid Id);

    public sealed record RawInboxOutput(Guid Id);

    /// <summary>What the handler observed on one entry.</summary>
    /// <param name="ContextUnit">The unit the consume context handed the handler.</param>
    /// <param name="BoundUnit">The unit the attempt's connection reports through <c>connection.UnitOfWork()</c>.</param>
    public sealed record HandlerEntry(IUnitOfWork ContextUnit, IUnitOfWork? BoundUnit);

    public sealed class ExecutionState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public required string InsertEffectSql { get; init; }
        public HandlerOutcome Outcome { get; init; }
        public bool ThrowOnlyOnFirstEntry { get; init; }
        public List<HandlerEntry> Entries { get; } = [];
        public Func<CancellationToken, Task>? BeforeHandlerReturns { get; set; }
    }

    public sealed class HandlerFailureException : Exception
    {
        public HandlerFailureException()
            : base("The handler failed after writing through the attempt unit.") { }
    }

    public sealed class RawInboxModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<RawInboxConsumer, RawInboxMessage>(
                "tests.raw-inbox.consumer",
                everyInstance: false,
                TestConsumerDispatch.FromServices<RawInboxConsumer, RawInboxMessage>()
            );
    }

    public sealed class RawInboxConsumer(ExecutionState state) : IConsume<RawInboxMessage>
    {
        public async ValueTask ConsumeAsync(
            ConsumeContext<RawInboxMessage> context,
            CancellationToken cancellationToken
        )
        {
            var unitOfWork =
                context.UnitOfWork
                ?? throw new InvalidOperationException(
                    "The transactional inbox guarantee must hand the consumer its unit."
                );
            var transaction = UnitOfWorkTransactions.RequireTransaction<DbTransaction>(
                unitOfWork,
                "raw inbox effect write"
            );
            var connection = ((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Connection;
            state.Entries.Add(new HandlerEntry(unitOfWork, connection.UnitOfWork()));
            var entry = state.Entries.Count;

            // The write joins the attempt only because it runs on the unit's connection with the unit's transaction.
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = state.InsertEffectSql;
                _AddParameter(command, "@id", context.Message.Id);
                _AddParameter(command, "@entry", entry);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var output = new RawInboxOutput(context.Message.Id);
            await unitOfWork.Outbox.PublishAsync(output, cancellationToken);
            await unitOfWork.Outbox.EnqueueAsync(output, cancellationToken);
            if (state.BeforeHandlerReturns is { } beforeHandlerReturns)
            {
                await beforeHandlerReturns(cancellationToken);
            }

            if (state.Outcome is HandlerOutcome.Throw && (!state.ThrowOnlyOnFirstEntry || entry == 1))
            {
                throw new HandlerFailureException();
            }
        }
    }
}
