// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Couchbase;
using Couchbase.Linq;
using Couchbase.Transactions;
using Headless.Couchbase.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Couchbase.ContextProvider;

/// <summary>
/// Constructs a <see cref="CouchbaseBucketContext"/> subclass and wires each
/// <c>IDocumentSet&lt;T&gt;</c> property to its Couchbase scope and collection, as declared by
/// <c>CouchbaseCollectionAttribute</c>. Uses a per-type compiled action cache for efficiency.
/// </summary>
[PublicAPI]
public static class CouchbaseBucketContextInitializer
{
    /// <summary>
    /// Creates an instance of <typeparamref name="TContext"/> via the DI activator and initializes its
    /// document-set properties.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="CouchbaseBucketContext"/> subclass to instantiate.</typeparam>
    /// <param name="serviceProvider">The service provider used to activate the context.</param>
    /// <param name="bucket">The Couchbase bucket the context is connected to.</param>
    /// <param name="transactions">The transaction manager for this bucket.</param>
    /// <param name="defaultScopeName">
    /// When non-null, all document sets are placed in this scope with their declared scope prepended
    /// to the collection name. Pass <see langword="null"/> to use each property's declared scope.
    /// </param>
    /// <returns>An initialized context with all document-set properties set.</returns>
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
    /// <remarks>
    /// Fragile: resolves Linq2Couchbase's INTERNAL <c>Couchbase.Linq.DocumentSet`1</c> type by name, and
    /// <c>_CreateInitializeAction</c> below instantiates it via its <c>(BucketContext, string, string)</c>
    /// constructor. A Linq2Couchbase upgrade that renames or reshapes either breaks this at runtime, not at
    /// compile time — re-verify on every Linq2Couchbase version bump.
    /// </remarks>
    private static readonly Type _DocumentSetType =
        typeof(IDocumentSet<>).Assembly.GetType("Couchbase.Linq.DocumentSet`1")
        ?? throw new InvalidOperationException("Could not find DocumentSet type.");

    private static readonly ConditionalWeakTable<Type, Action<CouchbaseBucketContext, string?>> _InitializeCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        Action<CouchbaseBucketContext, string?>
    >.CreateValueCallback _InitializeFactory = _CreateInitializeAction;

    private static Action<CouchbaseBucketContext, string?> _GetInitializeContextAction<TContext>()
    {
        return _InitializeCache.GetValue(typeof(TContext), _InitializeFactory);
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
