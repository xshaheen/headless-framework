// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Cluster;

[Collection(PostgreSqlClusterCollection.Name)]
public sealed class PostgreSqlClusterScenarios(PostgreSqlClusterFixture store) : ClusterScenariosBase(store);

[Collection(SqlServerClusterCollection.Name)]
public sealed class SqlServerClusterScenarios(SqlServerClusterFixture store) : ClusterScenariosBase(store);
