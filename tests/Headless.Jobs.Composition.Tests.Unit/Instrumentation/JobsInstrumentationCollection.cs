// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Instrumentation;

// Activity listeners are process-wide; other tests must not attach listeners or emit jobs while these assertions run.
[CollectionDefinition(DisableParallelization = true)]
public sealed class JobsInstrumentationCollection;
