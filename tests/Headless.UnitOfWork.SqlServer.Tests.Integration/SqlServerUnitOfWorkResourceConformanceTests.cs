// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the resource-backed conformance suite against a real SQL Server transaction.</summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerUnitOfWorkResourceConformanceTests(SqlServerUnitOfWorkFixture fixture)
    : UnitOfWorkResourceConformanceTests<SqlServerUnitOfWorkFixture>(fixture);
