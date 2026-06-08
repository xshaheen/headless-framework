// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.AmbientTransactions;
using Headless.Messaging.Storage.SqlServer.Diagnostics;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;

namespace Tests;

public sealed class SqlServerDiagnosticOutboxBufferTests : TestBase
{
    private const string _CommitAfter = "Microsoft.Data.SqlClient.WriteTransactionCommitAfter";
    private const string _RollbackAfter = "Microsoft.Data.SqlClient.WriteTransactionRollbackAfter";

    [Fact]
    public void should_complete_dispose_and_remove_buffered_transaction_after_commit()
    {
        // given
        var processor = new DiagnosticProcessorObserver();
        using var listener = _Subscribe(processor);
        using var connection = new SqlConnection();
        var transaction = new RecordingAmbientTransaction();
        processor.TransBuffer.TryAdd(connection.ClientConnectionId, transaction).Should().BeTrue();

        // when
        listener.Write(_CommitAfter, new SqlDiagnosticPayload(connection, Operation: "Commit"));

        // then
        processor.TransBuffer.Should().BeEmpty();
        transaction.CompletedExternally.Should().Be(1);
        transaction.Disposed.Should().Be(1);
    }

    [Fact]
    public void should_dispose_and_remove_buffered_transaction_after_rollback()
    {
        // given
        var processor = new DiagnosticProcessorObserver();
        using var listener = _Subscribe(processor);
        using var connection = new SqlConnection();
        var transaction = new RecordingAmbientTransaction();
        processor.TransBuffer.TryAdd(connection.ClientConnectionId, transaction).Should().BeTrue();

        // when
        listener.Write(_RollbackAfter, new SqlDiagnosticPayload(connection, Operation: "Rollback"));

        // then
        processor.TransBuffer.Should().BeEmpty();
        transaction.CompletedExternally.Should().Be(0);
        transaction.Disposed.Should().Be(1);
    }

    private static DiagnosticListener _Subscribe(DiagnosticProcessorObserver processor)
    {
        var listener = new DiagnosticListener(DiagnosticProcessorObserver.DiagnosticListenerName);
        processor.OnNext(listener);
        return listener;
    }

    private sealed record SqlDiagnosticPayload(SqlConnection Connection, string Operation);

    private sealed class RecordingAmbientTransaction : IAmbientTransaction
    {
        public int CompletedExternally { get; private set; }

        public int Disposed { get; private set; }

        public bool AutoCommit { get; set; }

        public object? DbTransaction { get; set; }

        public void RegisterCommitWork(Func<CancellationToken, ValueTask> drain) { }

        public void CompleteExternally()
        {
            CompletedExternally++;
        }

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
            Disposed++;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
