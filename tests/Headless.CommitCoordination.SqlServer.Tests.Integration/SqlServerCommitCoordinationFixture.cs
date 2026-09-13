// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// Collection fixture providing the SQL Server container shared by the commit coordination integration tests.
/// The tests share one probe table, so the collection runs serially.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerCommitCoordinationFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerCommitCoordinationFixture>;
