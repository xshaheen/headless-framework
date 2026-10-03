// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase.Linq;
using Headless.Couchbase.Context;

namespace Headless.Couchbase.Managers;

/// <summary>
/// Discovers Couchbase scope/collection pairs from assembly types, either by scanning assemblies
/// whose name starts with a given prefix, by a direct assembly list, or from live context instances.
/// </summary>
[PublicAPI]
public interface ICouchbaseAssemblyCollectionsReader
{
    /// <summary>
    /// Scans all non-system assemblies loaded in the current <see cref="AppDomain"/> whose full name
    /// starts with <paramref name="assemblyPrefix"/> and returns all Couchbase scope/collection pairs
    /// declared on <see cref="CouchbaseBucketContext"/> subclasses.
    /// </summary>
    /// <param name="assemblyPrefix">The assembly name prefix to filter on.</param>
    /// <returns>All discovered scope/collection pairs.</returns>
    IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix);

    /// <summary>Returns all scope/collection pairs declared across the given assemblies.</summary>
    /// <param name="assemblies">The assemblies to inspect.</param>
    /// <returns>All discovered scope/collection pairs.</returns>
    IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies);

    /// <summary>Returns all scope/collection pairs declared on the runtime types of the given contexts.</summary>
    /// <param name="contexts">Live context instances whose concrete types are introspected.</param>
    /// <returns>All discovered scope/collection pairs.</returns>
    IEnumerable<ScopeCollection> ReadCollections(List<CouchbaseBucketContext> contexts);
}
