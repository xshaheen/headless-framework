// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Stack;

namespace Tests.Scenarios;

[Collection(PostgreSqlChaosCollection.Name)]
public sealed class PostgreSqlChaosScenarios(PostgreSqlChaosFixture fixture) : ChaosScenariosBase(fixture);

[Collection(SqlServerChaosCollection.Name)]
public sealed class SqlServerChaosScenarios(SqlServerChaosFixture fixture) : ChaosScenariosBase(fixture);
