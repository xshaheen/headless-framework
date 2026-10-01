// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Serialization;
using Headless.Sql;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Persistence;

/// <summary>
/// The one relational messaging storage: the outbox, inbox, retry, delayed-dispatch, retention, and operator paths for
/// every relational engine, written once over <see cref="ISqlDialect" />. A provider package supplies the dialect and
/// its connection through <see cref="RelationalMessagingStorage" />, and its schema contribution creates the tables
/// <see cref="MessagingTables" /> names.
/// </summary>
/// <remarks>
/// Every time-based decision reads the database clock inside the statement that decides, never the application clock,
/// so replicas whose clocks disagree still agree on when a row falls due and when a lease ends.
/// </remarks>
#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.
internal sealed partial class RelationalDataStorage
    : IDataStorage,
        IDelayedMessageClaimStorage,
        IGracefulLeaseReleaseStorage,
        ICircuitRetryDeferralStorage,
        ITransactionalInboxStorage,
        IInboxOperationsApi,
        IScheduledDeliveryOperationsApi,
        IDeliveryCoordinationResolver,
        IRelationalOutboxStorage,
        IMessageRevocationStorage
{
    /// <summary>Delayed messages due within this window are claimed ahead of time for scheduling.</summary>
    private static readonly TimeSpan _DelayedMessageLookahead = TimeSpan.FromMinutes(2);

    /// <summary>Queued messages older than this are claimed again, in case their dispatch was lost.</summary>
    private static readonly TimeSpan _QueuedMessageLookback = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A command never carries more parameters than this, under SQL Server's limit of 2,100 per request, so a batch of
    /// per-row predicates is sized from the parameters each row needs.
    /// </summary>
    private const int _MaxCommandParameters = 2000;

    private const int _LeaseReleaseBatchSize = 400;

    /// <summary>The length SQL Server binds for an unbounded text parameter (<c>nvarchar(max)</c>).</summary>
    private const int _Unbounded = -1;

    private const int _NameMaxLength = 200;
    private const int _VersionMaxLength = 20;
    private const int _StatusMaxLength = 50;

    private static readonly SqlColumnType _ContentType = SqlColumnType.Text(_Unbounded);
    private static readonly SqlColumnType _NameType = SqlColumnType.Text(_NameMaxLength);
    private static readonly SqlColumnType _MessageIdType = SqlColumnType.KeyText(_NameMaxLength);
    private static readonly SqlColumnType _StatusType = SqlColumnType.Text(_StatusMaxLength);

    private readonly RelationalMessagingStorage _storage;
    private readonly ISqlDialect _dialect;
    private readonly MessagingTables _t;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly ISerializer _serializer;
    private readonly IGuidGenerator _guidGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly INodeMembership _nodeMembership;
    private readonly ILogger _logger;
    private readonly SqlColumnType _ownerType;
    private readonly string _publishedTable;
    private readonly string _receivedTable;
    private readonly string _terminalGuard;
    private readonly string _terminalGuardWithRetries;
    private readonly string _nextRetryAtAssignment;

    public RelationalDataStorage(
        RelationalMessagingStorage storage,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<MessagingStorageOptions> storageOptions,
        IStorageTableNames tableNames,
        ISerializer serializer,
        IGuidGenerator guidGenerator,
        TimeProvider timeProvider,
        INodeMembership nodeMembership,
        ILogger<RelationalDataStorage> logger
    )
    {
        _storage = Argument.IsNotNull(storage);
        _dialect = storage.Dialect;
        _t = MessagingTables.For(_dialect, Argument.IsNotNull(storageOptions));
        _messagingOptions = Argument.IsNotNull(messagingOptions);
        Argument.IsNotNull(tableNames);
        _serializer = Argument.IsNotNull(serializer);
        _guidGenerator = Argument.IsNotNull(guidGenerator);
        _timeProvider = Argument.IsNotNull(timeProvider);
        _nodeMembership = Argument.IsNotNull(nodeMembership);
        _logger = Argument.IsNotNull(logger);
        _ownerType = SqlColumnType.Text(storage.OwnerColumnMaxLength);
        _publishedTable = tableNames.GetPublishedTableName();
        _receivedTable = tableNames.GetReceivedTableName();

        // A row that is Succeeded or Failed with no retry pending is permanently done: no transition may rewrite it.
        // A Failed row that still has a NextRetryAt is persisted for retry and stays mutable, so the retry processor
        // can rewrite it on its next pickup.
        _terminalGuard =
            $"NOT ({_t.StatusName} IN ('{nameof(StatusName.Succeeded)}','{nameof(StatusName.Failed)}') AND {_t.NextRetryAt} IS NULL)";
        _terminalGuardWithRetries =
            $"{_terminalGuard} AND (@OriginalRetries IS NULL OR {_t.Retries}=@OriginalRetries) AND (@OriginalInlineAttempts IS NULL OR {_t.InlineAttempts}=@OriginalInlineAttempts)";

        // Cleared without a retry delay, otherwise the delay past the database clock, keeping a later stored due time
        // when the delay asks to. The database clock is the one retry pickup compares against, so an application
        // clock skewed from it cannot fire the retry early or late.
        var delayed = _dialect.ShiftByDuration(SqlDialectTokens.Now, "RetryDelay");
        _nextRetryAtAssignment =
            $"CASE WHEN @HasRetryDelay = {_t.True} THEN CASE WHEN @KeepsLaterDue = {_t.True} AND {_t.NextRetryAt} > {delayed} THEN {_t.NextRetryAt} ELSE {delayed} END END";
    }

    /// <summary>
    /// Creates the storage for <paramref name="storage"/> from the application's services, its message ids drawn from
    /// the sequential generator keyed by <paramref name="guidType"/>, the order the engine's primary key sorts best.
    /// </summary>
    public static RelationalDataStorage Create(
        IServiceProvider services,
        RelationalMessagingStorage storage,
        IStorageTableNames tableNames,
        SequentialGuidType guidType
    )
    {
        return new RelationalDataStorage(
            storage,
            services.GetRequiredService<IOptions<MessagingOptions>>(),
            services.GetRequiredService<IOptions<MessagingStorageOptions>>(),
            tableNames,
            services.GetRequiredService<ISerializer>(),
            services.GetRequiredKeyedService<IGuidGenerator>(guidType),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<INodeMembership>(),
            services.GetRequiredService<ILogger<RelationalDataStorage>>()
        );
    }

    private MessagingOptions Options => _messagingOptions.Value;

    private int CommandTimeoutSeconds => (int)Math.Min(Math.Ceiling(Options.CommandTimeout.TotalSeconds), int.MaxValue);

    DeliveryCoordination IDeliveryCoordinationResolver.Resolve(IUnitOfWork unitOfWork)
    {
        // A unit with no resource is not the same as no unit: this storage writes the row into the unit's own
        // relational transaction, so it has nothing to join and must say so rather than report "no unit" and
        // let the caller's row be written standalone outside the transaction they opened.
        if (unitOfWork.Resource is not IRelationalUnitOfWorkResource relational)
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
        }

        if (
            relational.Transaction is not { } transaction
            || !_dialect.TransactionType.IsInstanceOfType(transaction)
            || transaction.Connection is not { } connection
        )
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider);
        }

        // Npgsql keeps Connection populated after commit, so a committed-but-undisposed transaction passes the check
        // above; the resource knows its transaction finished, and a dead transaction must not be handed to the
        // outbox writer as joinable.
        if (relational.IsTransactionCompleted)
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.TransactionCompleted);
        }

        using var configuredConnection = _storage.CreateConnection();
        if (!RelationalDatabaseIdentity.IsSameDatabase(configuredConnection, connection))
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database);
        }

        return DeliveryCoordination.Compatible(unitOfWork, transaction);
    }

    DbConnection IRelationalOutboxStorage.CreateIdentityConnection() => _storage.CreateConnection();

    /// <summary>Returns the monitoring API for querying message statistics and dashboard data against this storage.</summary>
    public IMonitoringApi GetMonitoringApi()
    {
        return new RelationalMonitoringApi(_storage, _t, _messagingOptions, _serializer, _timeProvider);
    }

    public IInboxOperationsApi GetInboxOperationsApi() => this;

    public IScheduledDeliveryOperationsApi GetScheduledDeliveryOperationsApi() => this;

    /// <summary>Opens nothing: creates an unopened connection to the storage's database.</summary>
    private DbConnection _CreateConnection() => _storage.CreateConnection();

    private bool _IsReceived(string table) => string.Equals(table, _receivedTable, StringComparison.Ordinal);

    /// <summary>Equality that also holds when both sides are null.</summary>
    private static string _Same(string column, string parameter)
    {
        return $"({column} = @{parameter} OR ({column} IS NULL AND @{parameter} IS NULL))";
    }

    /// <summary>
    /// The fence an inbox attempt carries: a non-inbox row passes, and an inbox row passes only while it is still the
    /// attempt the caller holds, so a stale attempt cannot rewrite a generation another attempt now owns.
    /// </summary>
    private string _InboxAttemptGuard(bool matchStorageId)
    {
        var storageId = matchStorageId ? $"{_t.Id}=@InboxStorageId AND " : "";

        return $"({_t.IsInboxRecord} = {_t.False} OR ({storageId}{_t.IntentType}=@InboxIntentType AND {_t.Generation}=@InboxGeneration AND {_t.GenerationIncarnationId}=@InboxGenerationIncarnationId AND {_t.AttemptId}=@InboxAttemptId AND {_Same(_t.Owner, "InboxOwner")} AND {_t.LockedUntil}=@InboxLockedUntil))";
    }

    /// <summary>Binds the parameters <see cref="_InboxAttemptGuard"/> reads; all null for a non-inbox message.</summary>
    private void _BindInboxAttempt(DbCommand command, InboxAttemptFence? fence, bool matchStorageId)
    {
        if (matchStorageId)
        {
            _dialect.AddParameter(command, "InboxStorageId", SqlColumnType.Guid, fence?.StorageId);
        }

        _dialect.AddParameter(
            command,
            "InboxIntentType",
            SqlColumnType.Int16,
            fence is null ? null : MessageLaneCompatibility.ToPersistedValue(fence.Lane)
        );
        _dialect.AddParameter(command, "InboxGeneration", SqlColumnType.Int64, fence?.Generation);
        _dialect.AddParameter(
            command,
            "InboxGenerationIncarnationId",
            SqlColumnType.Guid,
            fence?.GenerationIncarnationId
        );
        _dialect.AddParameter(command, "InboxAttemptId", SqlColumnType.Guid, fence?.AttemptId);
        _dialect.AddParameter(command, "InboxOwner", _ownerType, fence?.Owner);
        _dialect.AddParameter(command, "InboxLockedUntil", SqlColumnType.Timestamp, fence?.LockedUntil);
    }

    /// <summary>
    /// Binds <see cref="MessagingOptions.Version"/> as data. Its validator bounds only its length, so the value may
    /// carry quotes and must never be spliced into SQL text.
    /// </summary>
    private void _BindVersion(DbCommand command)
    {
        _dialect.AddParameter(command, "Version", SqlColumnType.Text(_VersionMaxLength), Options.Version);
    }

    private void _BindOwner(DbCommand command, string parameter, bool hasLease)
    {
        _dialect.AddParameter(command, parameter, _ownerType, hasLease ? _nodeMembership.GetOwnerTag() : null);
    }

    /// <summary>Binds the parameters <see cref="_nextRetryAtAssignment"/> reads.</summary>
    private void _BindRetryDelay(DbCommand command, RetryDelay? retryDelay)
    {
        _dialect.AddParameter(command, "HasRetryDelay", SqlColumnType.Boolean, retryDelay.HasValue);
        _dialect.AddParameter(command, "KeepsLaterDue", SqlColumnType.Boolean, retryDelay?.KeepsLaterDue ?? false);
        _dialect.AddDuration(command, "RetryDelay", retryDelay?.Delay ?? TimeSpan.Zero);
    }

    /// <summary>
    /// Re-serializes a mutated envelope and re-establishes the <c>Content == Serialize(Origin)</c> invariant on
    /// the caller's in-memory copy, so a later retry pickup that reads the row sees what this write persisted.
    /// </summary>
    private string _RefreshContent(MediumMessage message)
    {
        var content = _serializer.Serialize(message.Origin);
        message.Content = content;

        return content;
    }

    /// <summary>
    /// Runs <paramref name="body"/> in the caller's transaction when one is given, otherwise in a transaction of its
    /// own, retried on a transient fault raised before its commit.
    /// </summary>
    private Task<T> _RunAsync<T>(
        DbTransaction? transaction,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        if (transaction is null)
        {
            return SqlAutonomousTransaction
                .RunAsync(_CreateConnection, body, _timeProvider, cancellationToken)
                .AsTask();
        }

        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The supplied DbTransaction has no active Connection — it may have already been committed or rolled back."
            );

        return body(connection, transaction, cancellationToken);
    }

    /// <summary>
    /// Runs a fenced transition of one row by its id and reports whether it applied, with the columns it wrote. The
    /// transition's fence and assignments read the database clock as <see cref="SqlDialectTokens.Now"/>.
    /// </summary>
    private Task<T> _TransitionAsync<T>(
        DbTransaction? transaction,
        string table,
        string fence,
        string set,
        IReadOnlyList<string> returning,
        Action<DbCommand> bind,
        Func<DbDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        var sql = _dialect.Render(
            new SqlFencedTransition(table, [new SqlKeyColumn(_t.Id, "Id")], fence, set, returning)
        );

        return _RunAsync(
            transaction,
            (connection, tx, ct) =>
                RelationalCommand.ExecuteReaderAsync(connection, tx, sql, CommandTimeoutSeconds, bind, read, ct),
            cancellationToken
        );
    }

    /// <summary>
    /// Deletes rows this transaction already holds locks on, one key seek per row: a list predicate an engine may answer
    /// with a scan would wait on rows other transactions hold, which the selection that locked these rows skipped.
    /// </summary>
    private async Task<int> _DeleteLockedAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string idColumn,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        var deleted = 0;
        foreach (var chunk in ids.Chunk(_MaxCommandParameters))
        {
            var list = string.Join(
                ",",
                Enumerable.Range(0, chunk.Length).Select(static i => "@Id" + i.ToString(CultureInfo.InvariantCulture))
            );
            deleted += await RelationalCommand
                .ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    $"DELETE FROM {table} WHERE {idColumn} IN ({list});",
                    CommandTimeoutSeconds,
                    command =>
                    {
                        for (var i = 0; i < chunk.Length; i++)
                        {
                            _dialect.AddParameter(
                                command,
                                "Id" + i.ToString(CultureInfo.InvariantCulture),
                                SqlColumnType.Guid,
                                chunk[i]
                            );
                        }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>Reads whether a fenced transition applied.</summary>
    private static async Task<bool> _ReadAppliedAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.GetBoolean(0);
    }

    private static async Task<DateTimeOffset?> _ReadInstantAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> _ReadStringAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal);
    }

    private static PoisonMessage _CreatePoisonMessage(Guid storageId, Exception exception)
    {
        return new(storageId, $"{exception.GetType().FullName}: {exception.Message}");
    }

    private readonly record struct PoisonMessage(Guid StorageId, string ExceptionInfo);
}
#pragma warning restore CA2100
