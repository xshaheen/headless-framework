// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Couchbase;
using Couchbase.Management.Eventing;
using Couchbase.Query;

namespace Headless.Couchbase.Clusters;

/// <summary>
/// Identifies a Couchbase storage location by bucket, scope, and collection name.
/// </summary>
[PublicAPI]
public readonly record struct CouchbaseKeyspace(string Bucket, string Scope, string Collection);
