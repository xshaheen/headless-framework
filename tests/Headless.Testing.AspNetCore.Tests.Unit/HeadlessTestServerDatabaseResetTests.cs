// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
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

    [Fact]
    public async Task should_retry_on_db_exception()
    {
        // Use a real SQLite connection for CreateAsync to succeed
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

        // Create a dummy table because Respawner throws if no tables are found
        var createCommand = connection.CreateCommand();
        createCommand.CommandText = "CREATE TABLE Dummy (Id INT PRIMARY KEY);";
        await createCommand.ExecuteNonQueryAsync(AbortToken);

        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(opt =>
        {
            opt.ConnectionProvider = _ => connection;
            opt.DbAdapter = Respawn.DbAdapter.Sqlite;
        });

        var callCount = 0;
        _server.ResetAction = (_, _) =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw Substitute.For<DbException>();
            }
            return Task.CompletedTask;
        };

        await _server.InitializeAsync();
        await _server.ResetDatabaseAsync(AbortToken);

        callCount.Should().Be(3);
    }
}
