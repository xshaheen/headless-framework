// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class AzureBlobStorageFixture : HeadlessAzuriteFixture, ICollectionFixture<AzureBlobStorageFixture>;
