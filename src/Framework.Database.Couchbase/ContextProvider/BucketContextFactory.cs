using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Couchbase;
using Couchbase.Transactions;
using Framework.Database.Couchbase.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Database.Couchbase.ContextProvider;

public static class BucketContextFactory
{
    public static TContext Create<TContext>(
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

    private static readonly ConcurrentDictionary<Type, Action<CouchbaseBucketContext, string?>> _InitializeCache = [];

    private static Action<CouchbaseBucketContext, string?> _GetInitializeContextAction<TContext>()
    {
        return _InitializeCache.GetOrAdd(typeof(TContext), static type => _CreateReflectionInitializeAction(type));
    }

    private static Action<CouchbaseBucketContext, string?> _CreateReflectionInitializeAction(Type childContextType)
    {
        return (context, defaultScopeName) =>
        {
            foreach (var property in _GetContextProperties(childContextType))
            {
                var (scope, collection) = _GetCollectionName(property.Info);

                scope ??= defaultScopeName;

                if (string.IsNullOrWhiteSpace(scope))
                {
                    throw new InvalidOperationException(
                        $"BucketContext {childContextType.Name} is missing scope name for {property.Info.Name}. Please either provide a default scope name or specify it on the attribute."
                    );
                }

                var documentSet = Activator.CreateInstance(
                    typeof(CouchbaseDocumentSet<>).MakeGenericType(property.DocumentType),
                    [context, scope, collection]
                );

                property.Info.SetValue(context, documentSet);
            }
        };
    }

    /// <summary>
    /// - The code is taken from the Couchbase Linq library and modified to work with our CouchbaseDocumentSet.
    ///
    /// Compiles an action which will initialize the CouchbaseBucketContext properties. This use a compiled action
    /// instead of reflection here for speed. This executes once the first time a given type is used,
    /// after which the action is reused for each new instance of StormBucketContext.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CouchbaseDocumentSet<>))]
    private static Action<CouchbaseBucketContext, string?> _CreateEmitInitializeAction(Type childContextType)
    {
        Type[] initializeArgumentTypes = [typeof(CouchbaseBucketContext), typeof(string)];
        Type[] documentSetConstructorArgumentTypes = [typeof(CouchbaseBucketContext), typeof(string), typeof(string)];

        // This creates a new dynamic method with the signature:
        // void Initialize(CouchbaseBucketContext context, string? defaultScopeName)

        var dynMethod = new DynamicMethod("Initialize", null, initializeArgumentTypes)
        {
            InitLocals = false // Don't need this, minor perf improvement
        };

        // Emit the IL to set each property on the context
        var il = dynMethod.GetILGenerator();
        il.DeclareLocal(childContextType);
        // Arguments:
        il.Emit(OpCodes.Ldarg_0); // Load argument 0 (CouchbaseBucketContext)
        il.Emit(OpCodes.Castclass, childContextType); // Cast to a child context type
        il.Emit(OpCodes.Stloc_0); // Store in local variable

        foreach (var property in _GetContextProperties(childContextType))
        {
            // Load the local variable (child context) for property setter
            il.Emit(OpCodes.Ldloc_0);
            // Duplicate the local variable (child context) for constructor
            il.Emit(OpCodes.Dup);

            // -- Create a new DocumentSet
            if (property.ScopeName is not null)
            {
                il.Emit(OpCodes.Ldstr, property.ScopeName); // Load scope name
            }
            else
            {
                il.Emit(OpCodes.Ldarg_1); // Load scope name from argument
            }

            // add if (string.IsNullOrWhiteSpace(scope)) throw new InvalidOperationException
            il.Emit(OpCodes.Call, typeof(string).GetMethod("IsNullOrWhiteSpace", [typeof(string)])!);
            var label = il.DefineLabel();
            il.Emit(OpCodes.Brfalse_S, label);
            il.Emit(
                OpCodes.Ldstr,
                $"BucketContext {childContextType.Name} is missing scope name for {property.Info.Name}. Please either provide a default scope name or specify it on the attribute."
            );
            il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(label);

            il.Emit(OpCodes.Ldstr, property.CollectionName); // Load collection name

            var documentSetType = typeof(CouchbaseDocumentSet<>).MakeGenericType(property.DocumentType);
            var documentSetConstructor = documentSetType.GetConstructor(documentSetConstructorArgumentTypes)!;
            il.Emit(OpCodes.Newobj, documentSetConstructor);

            var setter = property.Info.GetSetMethod()!; // Get property setter
            il.Emit(OpCodes.Callvirt, setter); // Call property setter
        }

        il.Emit(OpCodes.Ret); // Return

        return (Action<CouchbaseBucketContext, string?>)
            dynMethod.CreateDelegate(typeof(Action<CouchbaseBucketContext, string?>));
    }

    #endregion

    #region Get Properties Informations

    /// <summary>Properties that return <see cref="CouchbaseDocumentSet{TEntity}"/></summary>
    private static IEnumerable<DocumentInformation> _GetContextProperties(Type contextType)
    {
        var properties = contextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && _IsStormDocumentSet(p.PropertyType))
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

    private static (string? Scope, string Collection) _GetCollectionName(PropertyInfo property)
    {
        var attr =
            property.GetCustomAttribute<CollectionAttribute>()
            ?? throw new InvalidOperationException($"Missing {nameof(CollectionAttribute)} on {property.Name}.");

        var collection = attr.Collection;
        var scope = attr.Scope;

        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new InvalidOperationException(
                $"Invalid {nameof(CollectionAttribute.Collection)} on {property.Name}."
            );
        }

        return (scope, collection);
    }

    private static bool _IsStormDocumentSet(Type propertyType)
    {
        return propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(CouchbaseDocumentSet<>);
    }

    private sealed record DocumentInformation(
        PropertyInfo Info,
        Type DocumentType,
        string? ScopeName,
        string CollectionName
    );

    #endregion
}
