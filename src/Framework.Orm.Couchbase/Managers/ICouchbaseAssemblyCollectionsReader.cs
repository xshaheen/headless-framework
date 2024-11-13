// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Couchbase.Linq;
using Framework.Orm.Couchbase.Context;

namespace Framework.Orm.Couchbase.Managers;

public interface ICouchbaseAssemblyCollectionsReader
{
    IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix);

    IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies);

    IEnumerable<ScopeCollection> ReadCollections(List<CouchbaseBucketContext> contexts);
}

public sealed record ScopeCollection(string Scope, string Collection);

public sealed class CouchbaseAssemblyCollectionsReader : ICouchbaseAssemblyCollectionsReader
{
    public IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix)
    {
        var assemblies = _GetAssemblies(assemblyPrefix);

        return ReadCollections(assemblies);
    }

    public IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies)
    {
        var bucketContextType = typeof(CouchbaseBucketContext);
        var modulesTypes = assemblies.SelectMany(assembly => assembly.GetTypes());

        var contextTypes = modulesTypes.Where(type => type.IsClass && type.IsSubclassOf(bucketContextType));

        return _ReadCollections(contextTypes);
    }

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
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var stormAssemblies = assemblies.Where(assembly =>
            assembly is { IsDynamic: false, FullName: not null }
            && !_IsSystemAssembly(assembly.FullName)
            && assembly.FullName.StartsWith(assemblyPrefix, StringComparison.Ordinal)
        );

        return stormAssemblies;
    }

    private static bool _IsSystemAssembly(string? assemblyFullName)
    {
        return assemblyFullName?.StartsWith("System.", StringComparison.Ordinal) != false
            || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    #endregion
}
