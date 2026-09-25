// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerFeaturesFixture>]
public sealed class SqlServerFeaturesStorageConformanceTests(SqlServerFeaturesFixture fixture)
    : FeaturesStorageConformanceTests<SqlServerFeaturesFixture>(fixture);
