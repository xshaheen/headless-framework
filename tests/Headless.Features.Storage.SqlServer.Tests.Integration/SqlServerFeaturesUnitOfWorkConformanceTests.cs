// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerFeaturesFixture>]
public sealed class SqlServerFeaturesUnitOfWorkConformanceTests(SqlServerFeaturesFixture fixture)
    : FeaturesUnitOfWorkConformanceTests<SqlServerFeaturesFixture>(fixture);
