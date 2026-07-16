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

/// <summary>A Couchbase scope and collection name pair.</summary>
[PublicAPI]
public sealed record ScopeCollection(string Scope, string Collection);

/// <summary>Default <see cref="ICouchbaseAssemblyCollectionsReader"/> implementation.</summary>
[PublicAPI]
public sealed class CouchbaseAssemblyCollectionsReader : ICouchbaseAssemblyCollectionsReader
{
    /// <inheritdoc/>
    public IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix)
    {
        var assemblies = _GetAssemblies(assemblyPrefix);

        return ReadCollections(assemblies);
    }

    /// <inheritdoc/>
    public IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies)
    {
        var bucketContextType = typeof(CouchbaseBucketContext);
        var modulesTypes = assemblies.SelectMany(assembly => assembly.GetTypes());

        var contextTypes = modulesTypes.Where(type => type.IsClass && type.IsSubclassOf(bucketContextType));

        return _ReadCollections(contextTypes);
    }

    /// <inheritdoc/>
    public IEnumerable<ScopeCollection> ReadCollections(List<CouchbaseBucketContext> contexts)
    {
        var contextTypes = contexts.Select(context => context.GetType());

        return _ReadCollections(contextTypes);
    }

    #region Helpers

    private static IEnumerable<ScopeCollection> _ReadCollections(IEnumerable<Type> contextTypes)
    {
        var collectionAttributeType = typeof(CouchbaseCollectionAttribute);
        var documentSetType = typeof(IDocumentSet<>);

        var couchbaseTypes = contextTypes.SelectMany(contextType =>
            contextType
                .GetProperties()
                .Where(property =>
                    property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == documentSetType
                    && Attribute.IsDefined(property, collectionAttributeType)
                )
        );

        foreach (var documentSet in couchbaseTypes)
        {
            if (
                Attribute.GetCustomAttribute(documentSet, collectionAttributeType)
                is CouchbaseCollectionAttribute documentAttribute
            )
            {
                yield return new(documentAttribute.Scope, documentAttribute.Collection);
            }
        }
    }

    private static IEnumerable<Assembly> _GetAssemblies(string assemblyPrefix)
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(assembly =>
                assembly is { IsDynamic: false, FullName: not null }
                && !_IsSystemAssembly(assembly.FullName)
                && assembly.FullName.StartsWith(assemblyPrefix, StringComparison.Ordinal)
            );

        return assemblies;
    }

    private static bool _IsSystemAssembly(string? assemblyFullName)
    {
        return assemblyFullName?.StartsWith("System.", StringComparison.Ordinal) != false
            || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    #endregion
}
