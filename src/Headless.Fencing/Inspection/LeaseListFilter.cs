// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Fencing;

/// <summary>The validated filter an <see cref="ILeaseStore" /> list or count applies; every set part must match.</summary>
/// <param name="TenantId">
/// The stored tenant to read (empty for the host scope), or <see langword="null" /> for every tenant.
/// </param>
/// <param name="Kind">The one kind to read, or <see langword="null" /> for every kind.</param>
/// <param name="ResourcePrefix">
/// The literal prefix every resource starts with, compared ordinally, or <see langword="null" /> for every resource.
/// Never empty.
/// </param>
/// <param name="State">The state the leases must be in, judged by the store's clock.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record LeaseListFilter(
    string? TenantId,
    string? Kind,
    string? ResourcePrefix,
    FencedLeaseStateFilter State
);
