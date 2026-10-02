// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// SQLite leaf fixture for the <c>RunAsync</c> conformance suite and the provider-specific scenarios, over one database
/// file. The probe table is created outside any unit of work and probe counting uses an independent connection so it
/// observes committed state only. The tests share one probe table, so the collection runs serially.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteUnitOfWorkFixture
    : ICollectionFixture<SqliteUnitOfWorkFixture>,
        IUnitOfWorkRunFixture,
        IUnitOfWorkResourceFixture,
        IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public string ConnectionString => _database.ConnectionString;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    public UnitOfWorkResourceSession CreateSession(CapturingLoggerProvider? logs = null)
    {
        var provider = BuildProvider(logs);
        var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        return new UnitOfWorkResourceSession(provider, scope, manager);
    }

    public async ValueTask<UnitOfWorkResourceHandle> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        CancellationToken cancellationToken
    )
    {
#pragma warning disable CA2000 // Ownership moves to the returned handle; the catch covers the only path returning none.
        var connection = new SqliteConnection(ConnectionString);
#pragma warning restore CA2000

        try
        {
            var unitOfWork = await factory.BeginAsync(connection, cancellationToken: cancellationToken);

            return new UnitOfWorkResourceHandle(unitOfWork, connection);
        }
        catch
        {
            // A rejected begin (a second resource under an active unit) never returns a handle to release the
            // connection; the factory already rolled the transaction back, but the ADO object must still be
            // disposed here.
            await connection.DisposeAsync();

            throw;
        }
    }

    public async Task<UnitOfWorkObservedHandle> EnlistObservedAsync(
        IUnitOfWorkFactory factory,
        CancellationToken cancellationToken
    )
    {
#pragma warning disable CA2000 // Ownership moves to the returned handle; the catch covers the only path returning none.
        var connection = new SqliteConnection(ConnectionString);
#pragma warning restore CA2000
        await connection.OpenAsync(cancellationToken);
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var unitOfWork = factory.Enlist(connection, transaction);

        return new UnitOfWorkObservedHandle(unitOfWork, connection, transaction);
    }

    public ValueTask<IUnitOfWork> BeginOwnedOnAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqliteConnection)connection, cancellationToken: cancellationToken);
    }

    public IUnitOfWork EnlistOn(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((SqliteConnection)connection, (SqliteTransaction)transaction);
    }

    public Task RunOnAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        Func<IUnitOfWork, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        return factory.RunAsync((SqliteConnection)connection, operation, cancellationToken: cancellationToken);
    }

    public Task InsertProbeRowAsync(IUnitOfWork unitOfWork, string name, CancellationToken cancellationToken)
    {
        var resource =
            (IRelationalUnitOfWorkResource?)unitOfWork.Resource
            ?? throw new InvalidOperationException("The unit of work exposed no relational resource.");

        return InsertProbeRowAsync(
            (SqliteConnection)resource.Connection,
            (SqliteTransaction)resource.Transaction,
            name,
            cancellationToken
        );
    }

    public async Task RunAsync(
        Func<IUnitOfWorkRunContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqliteConnection(ConnectionString);

        await manager.RunAsync(
            connection,
            (unitOfWork, ct) => operation(new SqliteRunContext(connection, unitOfWork), ct),
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT count(*) FROM probe_rows", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS probe_rows (id INTEGER PRIMARY KEY, name TEXT); DELETE FROM probe_rows;",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts one probe row on <paramref name="transaction" />; shared by the provider-specific scenarios.</summary>
    public static async Task InsertProbeRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqliteCommand(
            "INSERT INTO probe_rows (name) VALUES (@name)",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static ServiceProvider BuildProvider(CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });
        services.AddSqliteUnitOfWork();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class SqliteRunContext(SqliteConnection connection, IUnitOfWork unitOfWork) : IUnitOfWorkRunContext
    {
        public IUnitOfWork UnitOfWork => unitOfWork;

        public Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            var transaction =
                (SqliteTransaction?)(unitOfWork.Resource as IRelationalUnitOfWorkResource)?.Transaction
                ?? throw new InvalidOperationException("The helper exposed no live relational transaction.");

            return SqliteUnitOfWorkFixture.InsertProbeRowAsync(connection, transaction, name, cancellationToken);
        }
    }
}
