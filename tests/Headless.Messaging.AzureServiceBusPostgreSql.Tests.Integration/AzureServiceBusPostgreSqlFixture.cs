// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class AzureServiceBusPostgreSqlFixture : MessagingStackFixtureBase
{
    public const string ConnectionStringEnvironmentVariable = "HEADLESS_TEST_AZURE_SERVICE_BUS_CONNECTION_STRING";

    private readonly string? _serviceBusConnectionString = Environment.GetEnvironmentVariable(
        ConnectionStringEnvironmentVariable
    );

    private readonly PostgreSqlComponent _postgreSql = new();

    public AzureServiceBusPostgreSqlFixture()
    {
        RegisterComponents(_postgreSql);
    }

    /// <summary>Returns the namespace credential, or skips the calling test when none was provided.</summary>
    public string RequireServiceBusConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_serviceBusConnectionString))
        {
            Assert.Skip(
                $"Azure Service Bus real-service tests require {ConnectionStringEnvironmentVariable}; no namespace credential was provided."
            );
        }

        return _serviceBusConnectionString!;
    }

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("AzureServiceBusPostgreSql", DisableParallelization = true)]
public sealed class AzureServiceBusPostgreSqlCollection : ICollectionFixture<AzureServiceBusPostgreSqlFixture>;
