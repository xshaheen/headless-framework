// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// The in-memory conformance collection. Tests run serially because the blocking scenarios measure how long a call
/// waits; the collection definition stays in this assembly because xUnit discovers it only in the test assembly.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class InMemoryIdempotencyConformanceFixture
    : InMemoryIdempotencyFixture,
        ICollectionFixture<InMemoryIdempotencyConformanceFixture>;
