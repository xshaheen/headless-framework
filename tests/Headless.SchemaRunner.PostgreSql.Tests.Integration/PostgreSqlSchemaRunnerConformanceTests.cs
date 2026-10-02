// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests;

/// <summary>The schema runner conformance suite against PostgreSQL.</summary>
[Collection<PostgreSqlSchemaRunnerFixture>]
public sealed class PostgreSqlSchemaRunnerConformanceTests(PostgreSqlSchemaRunnerFixture fixture)
    : SchemaRunnerConformanceTests(fixture);
