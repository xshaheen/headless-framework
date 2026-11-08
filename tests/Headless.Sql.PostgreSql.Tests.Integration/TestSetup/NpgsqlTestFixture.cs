// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class NpgsqlTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<NpgsqlTestFixture>;
