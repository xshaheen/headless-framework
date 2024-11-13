// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Framework.Orm.Couchbase.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.Couchbase.ContextProvider;

/// <summary>
/// The goal of this class to initialize any CouchbaseBucketContext properties that are
/// of type IDocumentSet{TEntity} with the correct scope and collection.
/// </summary>
public static class CouchbaseBucketContextInitializer
{
    public static TContext Initialize<TContext>(
        IServiceProvider serviceProvider,
        IBucket bucket,
        Transactions transactions,
        string? defaultScopeName
    )
        where TContext : CouchbaseBucketContext
    {
        var initialize = _GetInitializeContextAction<TContext>();
        var context = ActivatorUtilities.CreateInstance<TContext>(serviceProvider, [bucket, transactions]);

        initialize(context, defaultScopeName);

        return context;
    }

    #region Initialize Action

    /// <summary>Get internal type concrete DocumentSet{T} which exist in Couchbase Linq namespace</summary>
    private static readonly Type _DocumentSetType =
        typeof(IDocumentSet<>).Assembly.GetType("Couchbase.Linq.DocumentSet`1")
        ?? throw new InvalidOperationException("Could not find DocumentSet type.");

    private static readonly ConcurrentDictionary<Type, Action<CouchbaseBucketContext, string?>> _InitializeCache = [];

    private static Action<CouchbaseBucketContext, string?> _GetInitializeContextAction<TContext>()
    {
        return _InitializeCache.GetOrAdd(typeof(TContext), static type => _CreateInitializeAction(type));
    }

    private static Action<CouchbaseBucketContext, string?> _CreateInitializeAction(Type childContextType)
    {
        return (context, mainScope) =>
        {
            foreach (var property in _GetContextProperties(childContextType))
            {
                var (propertyScope, propertyCollection) = _GetCollectionName(property.Info);

                string scope;
                string collection;

                if (mainScope is not null) // Multi-tenant per scope
                {
                    scope = mainScope;
                    collection = propertyScope + "_" + propertyCollection;
                }
                else
                {
                    scope = propertyScope;
                    collection = propertyCollection;
                }

                // Create an instance of Document<T> with the correct scope and collection
                var documentSet = Activator.CreateInstance(
                    _DocumentSetType.MakeGenericType(property.DocumentType),
                    [context, scope, collection]
                );

                property.Info.SetValue(context, documentSet);
            }
        };
    }

    #endregion

    #region Get Properties Informations

    /// <summary>Properties that return <see cref="IDocumentSet{TEntity}"/></summary>
    private static IEnumerable<DocumentInformation> _GetContextProperties(Type contextType)
    {
        var properties = contextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && _IsDocumentSet(p.PropertyType))
            .Select(property =>
            {
                var (scope, collection) = _GetCollectionName(property);

                return new DocumentInformation(
                    Info: property,
                    DocumentType: property.PropertyType.GenericTypeArguments[0],
                    ScopeName: scope,
                    CollectionName: collection
                );
            });

        return properties;
    }

    private static (string Scope, string Collection) _GetCollectionName(PropertyInfo property)
    {
        var (scope, collection) =
            property.GetCustomAttribute<CouchbaseCollectionAttribute>()
            ?? throw new InvalidOperationException(
                $"Missing {nameof(CouchbaseCollectionAttribute)} on {property.Name}."
            );

        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException(
                $"Invalid {nameof(CouchbaseCollectionAttribute.Scope)} on {property.Name}."
            );
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new InvalidOperationException(
                $"Invalid {nameof(CouchbaseCollectionAttribute.Collection)} on {property.Name}."
            );
        }

        return (scope, collection);
    }

    private static bool _IsDocumentSet(Type propertyType)
    {
        return propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IDocumentSet<>);
    }

    private sealed record DocumentInformation(
        PropertyInfo Info,
        Type DocumentType,
        string ScopeName,
        string CollectionName
    );

    #endregion
}
