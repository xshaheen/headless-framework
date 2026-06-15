// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// Collection fixture providing a SQL Server container for the out-of-band commit-detection integration tests.
/// Parallelization is disabled because the SqlClient diagnostic listener lives in the process-global
/// <c>DiagnosticListener.AllListeners</c> registry — running these tests in parallel would let one test's diagnostic
/// subscription observe another test's commit edges, so the suite must run serially.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerCommitCoordinationFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerCommitCoordinationFixture>;
