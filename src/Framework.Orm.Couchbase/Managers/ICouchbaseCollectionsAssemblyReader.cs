using System.Reflection;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Linq;
using Framework.Orm.Couchbase.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.Couchbase.Managers;

public interface ICouchbaseCollectionsAssemblyReader
{
    IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix);

    IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies);

    IEnumerable<ScopeCollection> ReadCouchbaseBucketContextCollections(List<CouchbaseBucketContext> contexts);
}

public sealed record ScopeCollection(string? Scope, string Collection);

public sealed class CouchbaseCollectionsAssemblyReader(IServiceProvider serviceProvider)
    : ICouchbaseCollectionsAssemblyReader
{
    public IEnumerable<ScopeCollection> ReadCouchbaseBucketContextCollections(List<CouchbaseBucketContext> contexts)
    {
        var collectionAttributeType = typeof(CouchbaseCollectionAttribute);
        var documentSetType = typeof(CouchbaseDocumentSet<>);

        var couchbaseTypes = contexts.SelectMany(context =>
            context
                .GetType()
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
                is CollectionAttribute documentAttribute
            )
            {
                yield return new(documentAttribute.Scope, documentAttribute.Collection);
            }
        }
    }

    public IEnumerable<ScopeCollection> ReadCollections(string assemblyPrefix)
    {
        var assemblies = _GetAssemblies(assemblyPrefix);

        return ReadCollections(assemblies);
    }

    public IEnumerable<ScopeCollection> ReadCollections(IEnumerable<Assembly> assemblies)
    {
        var modulesTypes = assemblies.SelectMany(assembly => assembly.GetTypes());

        var collectionAttributeType = typeof(CollectionAttribute);
        var collectionProviderType = typeof(INamedCollectionProvider);
        var bucketContextType = typeof(BucketContext);

        var couchbaseTypes = modulesTypes.Where(type =>
            // subclass of BucketContext
            (type.IsClass && type.IsSubclassOf(bucketContextType))
            ||
            // an entity class
            (type.IsClass && Attribute.IsDefined(type, collectionAttributeType))
            ||
            // a named collection
            (type.IsInterface && type.GetInterfaces().Any(x => x == collectionProviderType))
        );

        foreach (var type in couchbaseTypes)
        {
            if (Attribute.GetCustomAttribute(type, collectionAttributeType) is CollectionAttribute entityAttribute)
            {
                yield return new(entityAttribute.Scope, entityAttribute.Collection);

                continue;
            }

            if (type.IsSubclassOf(bucketContextType))
            {
                var documentSets = type.GetProperties()
                    .Where(property =>
                        property.PropertyType.IsGenericType
                        && (
                            property.PropertyType.GetGenericTypeDefinition() == typeof(IDocumentSet<>)
                            || property.PropertyType.GetGenericTypeDefinition() == typeof(CouchbaseDocumentSet<>)
                        )
                        && Attribute.IsDefined(property, collectionAttributeType)
                    );

                foreach (var documentSet in documentSets)
                {
                    if (
                        Attribute.GetCustomAttribute(documentSet, collectionAttributeType)
                        is CollectionAttribute documentAttribute
                    )
                    {
                        yield return new(documentAttribute.Scope, documentAttribute.Collection);
                    }
                }

                continue;
            }

            var collectionProvider = (INamedCollectionProvider)serviceProvider.GetRequiredService(type);

            yield return new(collectionProvider.ScopeName, collectionProvider.CollectionName);
        }
    }

    #region Helpers

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
