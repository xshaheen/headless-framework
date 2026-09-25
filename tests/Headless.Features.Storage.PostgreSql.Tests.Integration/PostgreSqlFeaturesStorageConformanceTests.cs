// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlFeaturesFixture>]
public sealed class PostgreSqlFeaturesStorageConformanceTests(PostgreSqlFeaturesFixture fixture)
    : FeaturesStorageConformanceTests<PostgreSqlFeaturesFixture>(fixture);
