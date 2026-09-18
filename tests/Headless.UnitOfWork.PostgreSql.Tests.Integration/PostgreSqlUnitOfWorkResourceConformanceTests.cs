// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the resource-backed conformance suite against a real PostgreSQL transaction.</summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlUnitOfWorkResourceConformanceTests(PostgreSqlUnitOfWorkFixture fixture)
    : UnitOfWorkResourceConformanceTests<PostgreSqlUnitOfWorkFixture>(fixture);
