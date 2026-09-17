// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers;

/// <summary>
/// Serializes every test that resets or asserts on the process-static once-per-process ordering slot of
/// <c>TenantCatalogResolutionMiddleware</c>: with collections running in parallel, a concurrent class could
/// consume or reset the slot between another class's reset and its assertion.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TenantCatalogOrderingWarningCollection
{
    public const string Name = "tenant-catalog-ordering-warning";
}
