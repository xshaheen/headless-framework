// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Couchbase.Transactions;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// A connected Couchbase cluster together with its transaction manager, as returned by
/// <see cref="ICouchbaseClustersProvider.GetClusterAsync"/>.
/// </summary>
/// <remarks>
/// Deliberately non-positional so additional members can be added in future versions without
/// breaking consumers.
/// </remarks>
[PublicAPI]
public sealed record CouchbaseClusterConnection
{
    /// <summary>Gets the connected cluster.</summary>
    public required ICluster Cluster { get; init; }

    /// <summary>Gets the transaction manager bound to <see cref="Cluster"/>.</summary>
    public required Transactions Transactions { get; init; }
}
