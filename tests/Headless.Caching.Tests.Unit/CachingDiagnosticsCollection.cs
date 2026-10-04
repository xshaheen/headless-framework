// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// ActivityListener is process-global, so diagnostics tests that install listeners cannot overlap tests that assert
// the listener-free fast path.
[CollectionDefinition(Name)]
public sealed class CachingDiagnosticsCollection
{
    public const string Name = "Caching diagnostics";
}
