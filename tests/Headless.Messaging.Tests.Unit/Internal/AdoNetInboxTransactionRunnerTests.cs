// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Internal;

/// <summary>
/// The raw ADO.NET inbox runner owns the connection and transaction of every attempt, so a fault anywhere in the
/// attempt must still return the pooled connection.
/// </summary>
public sealed class AdoNetInboxTransactionRunnerTests : TestBase
{
    private readonly ITransactionalInboxStorage _storage = Substitute.For<ITransactionalInboxStorage>();
    private readonly IDeliveryCoordinationResolver _resolver = Substitute.For<IDeliveryCoordinationResolver>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly MediumMessage _message = new()
    {
        StorageId = Guid.NewGuid(),
        Origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString(),
                [Headers.MessageName] = "test.messageName",
            },
            "{}"
        ),
        Content = "{}",
        Lane = MessageLane.Bus,
        Added = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task should_dispose_the_connection_when_opening_it_fails()
    {
        // given
        await using var connection = new FakeConnection { OpenFault = new InvalidOperationException("open failed") };
        var handlerEntered = false;
        var runner = _CreateRunner(connection, (_, _) => _unitOfWork);

        // when
        var act = () =>
            runner.ExecuteAsync(
                _message,
                (_, _) =>
                {
                    handlerEntered = true;
                    return Task.CompletedTask;
                },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("open failed");
        connection.WasDisposed.Should().BeTrue();
        handlerEntered.Should().BeFalse();
    }

    [Fact]
    public async Task should_dispose_the_transaction_and_connection_when_enlisting_fails()
    {
        // given
        await using var connection = new FakeConnection();
        var runner = _CreateRunner(connection, (_, _) => throw new InvalidOperationException("already bound"));

        // when
        var act = () => runner.ExecuteAsync(_message, (_, _) => Task.CompletedTask, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("already bound");
        connection.Transaction!.WasDisposed.Should().BeTrue();
        connection.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_the_transaction_and_connection_when_disposing_the_unit_fails()
    {
        // given: a committed attempt whose unit faults while being disposed
        await using var connection = new FakeConnection();
        _unitOfWork.DisposeAsync().Returns(_ => throw new InvalidOperationException("unit dispose failed"));
        _storage
            .CompleteReceivedInboxAsync(_message, Arg.Any<DbTransaction>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        var runner = _CreateRunner(connection, (_, _) => _unitOfWork);

        // when
        var act = () => runner.ExecuteAsync(_message, (_, _) => Task.CompletedTask, AbortToken);

        // then: the fault surfaces after entry, and neither resource leaks
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unit dispose failed");
        connection.Transaction!.Committed.Should().BeTrue();
        connection.Transaction.WasDisposed.Should().BeTrue();
        connection.WasDisposed.Should().BeTrue();
    }

    private AdoNetInboxTransactionRunner _CreateRunner(
        FakeConnection connection,
        Func<DbConnection, DbTransaction, IUnitOfWork> enlist
    )
    {
        _resolver
            .Resolve(_unitOfWork)
            .Returns(_ => DeliveryCoordination.Compatible(_unitOfWork, connection.Transaction));

        return new AdoNetInboxTransactionRunner(() => connection, enlist, _resolver, _storage, NullLogger.Instance);
    }

    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public Exception? OpenFault { get; init; }

        public FakeTransaction? Transaction { get; private set; }

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "test";

        public override string DataSource => "test";

        public override string ServerVersion => "1";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open()
        {
            if (OpenFault is not null)
            {
                throw OpenFault;
            }

            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            Transaction = new FakeTransaction(this, isolationLevel);
            return Transaction;
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeTransaction(DbConnection connection, IsolationLevel isolationLevel) : DbTransaction
    {
        public bool Committed { get; private set; }

        public bool WasDisposed { get; private set; }

        public override IsolationLevel IsolationLevel => isolationLevel;

        protected override DbConnection DbConnection => connection;

        public override void Commit() => Committed = true;

        public override void Rollback() { }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
