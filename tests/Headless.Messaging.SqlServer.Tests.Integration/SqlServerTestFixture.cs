// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// Collection fixture providing a SQL Server container for integration tests.
/// Uses the shared <see cref="HeadlessSqlServerFixture"/> for ARM64/x64 compatibility.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerTestFixture : HeadlessSqlServerFixture, ICollectionFixture<SqlServerTestFixture>;
