// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts;

namespace Headless.EntityFramework;

/// <summary>
/// Captures the root service provider, so the runtime can tell whether the application provider on a context's
/// options is the root or a scope. A singleton receives the root, which is also what EF stores on singleton (pooled)
/// options; per-scope options carry the scope that built them.
/// </summary>
internal sealed class HeadlessRootServiceProvider(IServiceProvider services)
{
    public IServiceProvider Services { get; } = services;
}
