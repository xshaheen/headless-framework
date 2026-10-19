// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Tests.Fixtures;

namespace Tests;

[UsedImplicitly]
public sealed class AwsPostgreSqlFixture : MessagingStackFixtureBase
{
    private readonly BrokerComponent _localStack = new();
    private readonly PostgreSqlComponent _postgreSql = new();

    public AwsPostgreSqlFixture()
    {
        RegisterComponents(_localStack, _postgreSql);
    }

    public string LocalStackConnectionString => _localStack.Container.GetConnectionString();

    public string StorageConnectionString => _postgreSql.ConnectionString;

    public Task ResetAsync() => _postgreSql.ResetAsync();

    /// <summary>Freezes the broker, so the host's connections stay open but nothing reaches it.</summary>
    public Task PauseBrokerAsync(CancellationToken cancellationToken) =>
        _localStack.Container.PauseAsync(cancellationToken);

    public Task UnpauseBrokerAsync(CancellationToken cancellationToken) =>
        _localStack.Container.UnpauseAsync(cancellationToken);

    private sealed class BrokerComponent : HeadlessLocalStackFixture;

    private sealed class PostgreSqlComponent : PostgreSqlStackComponent;
}

[CollectionDefinition("AwsPostgreSql", DisableParallelization = true)]
public sealed class AwsPostgreSqlCollection : ICollectionFixture<AwsPostgreSqlFixture>;
