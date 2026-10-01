// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>The schema runner conformance suite against SQLite.</summary>
[Collection<SqliteSchemaRunnerFixture>]
public sealed class SqliteSchemaRunnerConformanceTests(SqliteSchemaRunnerFixture fixture)
    : SchemaRunnerConformanceTests(fixture);
