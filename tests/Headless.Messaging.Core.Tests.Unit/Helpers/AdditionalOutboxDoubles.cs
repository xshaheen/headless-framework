// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;

namespace Tests.Helpers;

/// <summary>Substitutes for a relational storage and for an additional outbox built on one.</summary>
internal static class AdditionalOutboxDoubles
{
    /// <summary>
    /// A storage that joins units of work, reports its database identity, claims delayed rows, and revokes, the
    /// capabilities the PostgreSQL and SQL Server storages carry.
    /// </summary>
    public static IDataStorage CreateRelationalStorage(string database, string dataSource = "db-host:5432")
    {
        var storage = (IDataStorage)
            Substitute.For(
                [
                    typeof(IDataStorage),
                    typeof(IDeliveryCoordinationResolver),
                    typeof(IRelationalOutboxStorage),
                    typeof(IDelayedMessageClaimStorage),
                    typeof(IMessageRevocationStorage),
                ],
                []
            );
        ((IRelationalOutboxStorage)storage)
            .CreateIdentityConnection()
            .Returns(_ => new FakeDbConnection(dataSource, database));

        return storage;
    }

    /// <summary>
    /// An additional outbox, initialized by default as the bootstrapper leaves one whose database was reachable at
    /// startup. Pass <paramref name="initialized"/> <see langword="false"/> for one whose database was not.
    /// </summary>
    public static MessagingOutbox CreateOutbox(
        string database,
        string dataSource = "db-host:5432",
        string? name = null,
        bool initialized = true
    )
    {
        var initializer = Substitute.For<IStorageInitializer>();
        initializer.GetPublishedTableName().Returns($"{database}.published");

        var outbox = new MessagingOutbox(
            name ?? $"AddOutbox().UseEntityFramework<{database}>()",
            CreateRelationalStorage(database, dataSource),
            initializer
        );

        // The substitute initializer returns a completed task, so the initialization completes before returning.
        if (initialized && !outbox.EnsureInitializedAsync(CancellationToken.None).IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The substitute outbox initialization did not complete synchronously.");
        }

        return outbox;
    }

    public static MessagingOutboxes CreateOutboxes(IDataStorage primary, params MessagingOutbox[] secondaries)
    {
        return new MessagingOutboxes(primary, primary as IDeliveryCoordinationResolver, secondaries);
    }

    /// <summary>A connection that is never opened; it only carries the identity the outbox validation reads.</summary>
    public sealed class FakeDbConnection(string dataSource, string database) : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => database;

        public override string DataSource => dataSource;

        public override string ServerVersion => "1";

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
