// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Collection definition to ensure sequential execution of tests that share static state.
/// The InMemoryDataStorage uses static ConcurrentDictionary fields for storage.
/// </summary>
[CollectionDefinition("InMemoryStorage", DisableParallelization = true)]
public sealed class InMemoryStorageCollectionFixture;
