// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Headless.Checks;
using Headless.Couchbase.Clusters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Couchbase.Managers;

/// <summary>
/// Provides idempotent scope, collection, and index management operations against a Couchbase cluster.
/// All mutating operations are guarded by a Polly retry pipeline (linear back-off with jitter plus an
/// overall timeout, configured via <see cref="CouchbaseManagerOptions"/>); transient failures are retried
/// while idempotent exceptions (scope/collection/index already exists) are treated as success.
/// </summary>
[PublicAPI]
public interface ICouchbaseManager
{
    /// <summary>
    /// Creates the named scope in the bucket if it does not already exist.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket in which to create the scope.</param>
    /// <param name="scopeName">The name of the scope to create.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// <see cref="CreateScopeStatus.Exist"/> when the scope already exists,
    /// <see cref="CreateScopeStatus.Success"/> when it was created, or
    /// <see cref="CreateScopeStatus.Failed"/> when all retries were exhausted.
    /// </returns>
    Task<CreateScopeStatus> CreateScopeAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates any collections in <paramref name="collections"/> that do not yet exist within the scope,
    /// then ensures each has a primary index.
    /// </summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket containing the scope.</param>
    /// <param name="scopeName">The scope in which to create collections.</param>
    /// <param name="collections">
    /// The set of collection names to ensure exist. Set semantics are required: the names are processed
    /// in parallel, so they must be unique.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task CreateCollectionsAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        IReadOnlySet<string> collections,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates a named secondary index on a collection if it does not already exist.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket containing the collection.</param>
    /// <param name="scopeName">The scope containing the collection.</param>
    /// <param name="collectionName">The collection to index.</param>
    /// <param name="indexName">The name to assign to the index.</param>
    /// <param name="fields">The fields to include in the index.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task CreateSecondaryIndexAsync(
        string clusterKey,
        string bucketName,
        string scopeName,
        string collectionName,
        string indexName,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default
    );

    /// <summary>Triggers the build of all deferred indexes on the bucket.</summary>
    /// <param name="clusterKey">The logical cluster identifier.</param>
    /// <param name="bucketName">The bucket whose deferred indexes should be built.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task BuildDeferredIndexesAsync(string clusterKey, string bucketName, CancellationToken cancellationToken = default);
}
