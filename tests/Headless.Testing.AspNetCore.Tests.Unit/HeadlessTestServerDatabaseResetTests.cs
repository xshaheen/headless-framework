// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Net.Sockets;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;

namespace Tests;

public sealed class HeadlessTestServerDatabaseResetTests : TestBase
{
    private HeadlessTestServer<Program>? _server;

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_throw_when_database_reset_not_configured()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var act = async () => await _server.ResetDatabaseAsync();

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task should_throw_when_connection_provider_is_null()
    {
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(_ => { });
        await _server.InitializeAsync();

        var act = async () => await _server.ResetDatabaseAsync();

        await act.Should()
            .ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*ConnectionProvider must be set*");
    }

    [Fact]
    public async Task should_throw_when_reset_after_dispose()
    {
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(_ => { });
        await _server.InitializeAsync();
        await _server.DisposeAsync();

        var act = async () => await _server.ResetDatabaseAsync();

        await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_return_self_from_configure_database_reset()
    {
        _server = new HeadlessTestServer<Program>();

        var result = _server.ConfigureDatabaseReset(_ => { });

        result.Should().BeSameAs(_server);
    }

    public static TheoryData<string> TransientResetExceptions => ["database", "io", "socket", "wrapped-socket"];

    [Theory]
    [MemberData(nameof(TransientResetExceptions))]
    public async Task should_retry_on_transient_reset_exception(string exceptionKind)
    {
        // Use a real SQLite connection for CreateAsync to succeed
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);

        var connectionProviderCalls = 0;
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(opt =>
        {
            opt.ConnectionProvider = _ =>
            {
                connectionProviderCalls++;
                return connectionProviderCalls == 1 ? connection : new SqliteConnection("Data Source=:memory:");
            };
            opt.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        var callCount = 0;
        _server.ResetAction = (_, _, _) =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw _CreateTransientException(exceptionKind);
            }
            return Task.CompletedTask;
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        callCount.Should().Be(3);
        connectionProviderCalls.Should().Be(3);
    }

    [Fact]
    public async Task should_retry_provider_specific_exception_when_configured()
    {
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);
        var connectionProviderCalls = 0;
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ =>
            {
                connectionProviderCalls++;
                return connectionProviderCalls == 1 ? connection : new SqliteConnection("Data Source=:memory:");
            };
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
            options.AdditionalTransientExceptionFilter = exception => exception is InvalidOperationException;
        });

        var callCount = 0;
        _server.ResetAction = (_, _, _) =>
        {
            callCount++;
            return callCount == 1
                ? Task.FromException(new InvalidOperationException("Provider-specific connection failure."))
                : Task.CompletedTask;
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        callCount.Should().Be(2);
        connectionProviderCalls.Should().Be(2);
    }

    [Fact]
    public async Task should_not_retry_non_transient_exception()
    {
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ => connection;
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        var callCount = 0;
        _server.ResetAction = (_, _, _) =>
        {
            callCount++;
#pragma warning disable MA0015 // The reset action has no parameter; this simulates a dependency validation failure.
            return Task.FromException(new ArgumentException("Invalid reset configuration."));
#pragma warning restore MA0015
        };

        await _server.InitializeAsync();
        var act = async () => await _server.ResetDatabaseAsync(AbortToken);

        await act.Should().ThrowExactlyAsync<ArgumentException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_replace_closed_connection_through_public_reset_path()
    {
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateSharedDatabaseAsync(connectionString);
        var connections = new List<SqliteConnection>();
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ =>
            {
                var connection = new SqliteConnection(connectionString);
                connections.Add(connection);
                return connection;
            };
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);
        await _InsertDummyAsync(keeper, 1);
        await connections[0].CloseAsync();

        await _server.ResetDatabaseAsync(AbortToken);

        connections.Should().HaveCount(2);
        (await _CountDummyAsync(keeper)).Should().Be(0);
    }

    [Fact]
    public async Task should_wrap_final_transient_exception_after_retry_exhaustion()
    {
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateSharedDatabaseAsync(connectionString);
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ => new SqliteConnection(connectionString);
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });
        var resetCalls = 0;
        var transientException = Substitute.For<DbException>();
        _server.ResetAction = (_, _, _) =>
        {
            resetCalls++;
            return Task.FromException(transientException);
        };

        await _server.InitializeAsync();
        var act = async () => await _server.ResetDatabaseAsync(AbortToken);

        var exception = await act.Should()
            .ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("Database reset failed after 3 attempts.");
        exception.Which.InnerException.Should().BeSameAs(transientException);
        resetCalls.Should().Be(3);
    }

    [Fact]
    public async Task should_dispose_failed_initial_connection_before_recovering()
    {
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateSharedDatabaseAsync(connectionString);
        var failedConnection = Substitute.For<DbConnection>();
        failedConnection
            .OpenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Connection initialization failed.")));
        var providerCalls = 0;
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ =>
                ++providerCalls == 1 ? failedConnection : new SqliteConnection(connectionString);
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        await _server.InitializeAsync();
        var firstReset = async () => await _server.ResetDatabaseAsync(AbortToken);

        await firstReset.Should().ThrowExactlyAsync<IOException>();
        await _server.ResetDatabaseAsync(AbortToken);

        providerCalls.Should().Be(2);
        failedConnection
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(DbConnection.DisposeAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_retry_when_replacement_connection_open_fails_transiently()
    {
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateSharedDatabaseAsync(connectionString);
        var failedReplacement = Substitute.For<DbConnection>();
        failedReplacement
            .OpenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(Substitute.For<DbException>()));
        var providerCalls = 0;
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ =>
                ++providerCalls switch
                {
                    1 => new SqliteConnection(connectionString),
                    2 => failedReplacement,
                    _ => new SqliteConnection(connectionString),
                };
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });
        var resetCalls = 0;
        _server.ResetAction = (_, _, _) =>
        {
            resetCalls++;
            return resetCalls == 1 ? Task.FromException(Substitute.For<DbException>()) : Task.CompletedTask;
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        providerCalls.Should().Be(3);
        resetCalls.Should().Be(2);
        failedReplacement
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(DbConnection.DisposeAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_reset_real_database_after_transient_failure_replaces_connection()
    {
        var connectionString = _CreateSharedDatabaseConnectionString();
        await using var keeper = await _CreateSharedDatabaseAsync(connectionString);
        await _InsertDummyAsync(keeper, 1);
        var providerCalls = 0;
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(options =>
        {
            options.ConnectionProvider = _ =>
            {
                providerCalls++;
                return new SqliteConnection(connectionString);
            };
            options.DbAdapter = Respawn.DbAdapter.Sqlite;
        });
        var resetCalls = 0;
        _server.ResetAction = (reset, connection, cancellationToken) =>
        {
            resetCalls++;
            if (resetCalls == 1)
            {
                return Task.FromException(new IOException("Transport interrupted."));
            }

            return reset.ResetAsync(connection, cancellationToken);
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        providerCalls.Should().Be(2);
        resetCalls.Should().Be(2);
        (await _CountDummyAsync(keeper)).Should().Be(0);
    }

    [Fact]
    public async Task should_use_test_context_cancellation_token_by_default()
    {
        await using var connection = await TestSqliteConnection.CreateAsync(AbortToken);

        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(opt =>
        {
            opt.ConnectionProvider = _ => connection;
            opt.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        var capturedToken = CancellationToken.None;
        _server.ResetAction = (_, _, cancellationToken) =>
        {
            capturedToken = cancellationToken;
            return Task.CompletedTask;
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        capturedToken.Should().Be(AbortToken);
    }

    private static string _CreateSharedDatabaseConnectionString()
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = $"headless-reset-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 1,
        }.ToString();
    }

    private static Exception _CreateTransientException(string exceptionKind)
    {
        return exceptionKind switch
        {
            "database" => Substitute.For<DbException>(),
            "io" => new IOException("Transport interrupted."),
            "socket" => new SocketException((int)SocketError.ConnectionReset),
            "wrapped-socket" => new InvalidOperationException(
                "The connection is broken.",
                new SocketException((int)SocketError.ConnectionReset)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind), exceptionKind, null),
        };
    }

    private static async Task<SqliteConnection> _CreateSharedDatabaseAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Dummy (Id INTEGER PRIMARY KEY);";
        await command.ExecuteNonQueryAsync(AbortToken);
        return connection;
    }

    private static async Task _InsertDummyAsync(SqliteConnection connection, int id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Dummy (Id) VALUES ($id);";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private static async Task<long> _CountDummyAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Dummy;";
        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
