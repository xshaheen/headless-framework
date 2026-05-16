// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class TusAzureFixture : HeadlessAzuriteFixture, ICollectionFixture<TusAzureFixture>;
