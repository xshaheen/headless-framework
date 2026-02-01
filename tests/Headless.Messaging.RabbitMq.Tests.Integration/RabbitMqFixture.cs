// Copyright (c) Mahmoud Shaheen. All rights reserved.

using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>
/// Collection fixture providing a RabbitMQ container for integration tests.
/// Uses Testcontainers.RabbitMq for container lifecycle management.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class RabbitMqFixture(IMessageSink messageSink)
    : ContainerFixture<RabbitMqBuilder, RabbitMqContainer>(messageSink),
        ICollectionFixture<RabbitMqFixture>
{
    private IConnection? _connection;

    /// <summary>Gets the RabbitMQ connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    /// <summary>Gets the RabbitMQ hostname.</summary>
    public string HostName => Container.Hostname;

    /// <summary>Gets the RabbitMQ port.</summary>
    public int Port => Container.GetMappedPublicPort(5672);

    /// <summary>Gets the RabbitMQ username.</summary>
    public string UserName => RabbitMqBuilder.DefaultUsername;

    /// <summary>Gets the RabbitMQ password.</summary>
    public string Password => RabbitMqBuilder.DefaultPassword;

    /// <summary>Gets or creates a shared connection to RabbitMQ.</summary>
    public async Task<IConnection> GetConnectionAsync()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        var factory = new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            UserName = UserName,
            Password = Password,
        };

        _connection = await factory.CreateConnectionAsync();
        return _connection;
    }

    protected override RabbitMqBuilder Configure()
    {
        return base.Configure().WithImage("rabbitmq:3-alpine");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        await base.DisposeAsyncCore();
    }
}
