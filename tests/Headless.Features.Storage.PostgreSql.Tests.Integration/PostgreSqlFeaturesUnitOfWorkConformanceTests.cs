// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlFeaturesFixture>]
public sealed class PostgreSqlFeaturesUnitOfWorkConformanceTests(PostgreSqlFeaturesFixture fixture)
    : FeaturesUnitOfWorkConformanceTests<PostgreSqlFeaturesFixture>(fixture);
