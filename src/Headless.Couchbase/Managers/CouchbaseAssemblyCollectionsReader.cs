// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase.Linq;
using Headless.Couchbase.Context;

namespace Headless.Couchbase.Managers;

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
