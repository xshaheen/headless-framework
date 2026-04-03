// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.AspNetCore;

namespace Tests;

public sealed class HeadlessTestServerDatabaseResetTests : IAsyncLifetime
{
    private HeadlessTestServer<Program>? _server;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_throw_when_database_reset_not_configured()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        Func<Task> act = async () => await _server.ResetDatabaseAsync();

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task should_throw_when_connection_provider_is_null()
    {
        _server = new HeadlessTestServer<Program>();
        _server.ConfigureDatabaseReset(_ => { });
        await _server.InitializeAsync();

        Func<Task> act = async () => await _server.ResetDatabaseAsync();

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

        Func<Task> act = async () => await _server.ResetDatabaseAsync();

        await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_return_self_from_configure_database_reset()
    {
        _server = new HeadlessTestServer<Program>();

        var result = _server.ConfigureDatabaseReset(_ => { });

        result.Should().BeSameAs(_server);
    }
}
