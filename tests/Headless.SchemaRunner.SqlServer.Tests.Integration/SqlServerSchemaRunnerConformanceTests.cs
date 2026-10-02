// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests;

/// <summary>The schema runner conformance suite against SQL Server.</summary>
[Collection<SqlServerSchemaRunnerFixture>]
public sealed class SqlServerSchemaRunnerConformanceTests(SqlServerSchemaRunnerFixture fixture)
    : SchemaRunnerConformanceTests(fixture);
