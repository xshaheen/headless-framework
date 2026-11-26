// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Logging.Internal;

/// <summary>
/// Types the classified-object walk never reads, because reading their members has effects: it blocks, runs a factory
/// or a query, or allocates. They print as their type name.
/// </summary>
/// <remarks>
/// Infrastructure from libraries this package does not reference (EF Core, ASP.NET Core, NHibernate, Castle proxies) is
/// matched by full type name, so recognizing it costs no dependency.
/// </remarks>
internal static class WalkLeaves
{
    // A class or any of its base classes with one of these names. A DbContext's getters reach its Database, Model, and
    // sets; an entry's reach the change tracker and every navigation; an HTTP context's reach the request body and the
    // service provider.
    private static readonly HashSet<string> _BaseNames = new(StringComparer.Ordinal)
    {
        "Microsoft.EntityFrameworkCore.DbContext",
        "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
        "Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker",
        "Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry",
        "Microsoft.EntityFrameworkCore.ChangeTracking.MemberEntry",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.AspNetCore.Http.HttpRequest",
        "Microsoft.AspNetCore.Http.HttpResponse",
        "Microsoft.AspNetCore.Http.ConnectionInfo",
    };

    // A type implementing one of these. A lazy-loading proxy loads a navigation from inside its getter, and an
    // NHibernate collection loads on enumeration although it is a list; a model's getters reach every entity type.
    private static readonly HashSet<string> _InterfaceNames = new(StringComparer.Ordinal)
    {
        "Microsoft.EntityFrameworkCore.Metadata.IReadOnlyModel",
        "Microsoft.EntityFrameworkCore.Infrastructure.ILazyLoader",
        "Microsoft.EntityFrameworkCore.Proxies.Internal.IProxyLazyLoader",
        "Castle.DynamicProxy.IProxyTargetAccessor",
        "NHibernate.Proxy.INHibernateProxy",
        "NHibernate.Collection.IPersistentCollection",
    };

    private const string _LazyLoaderName = "Microsoft.EntityFrameworkCore.Infrastructure.ILazyLoader";

    public static bool IsLeaf(Type type) =>
        typeof(Task).IsAssignableFrom(type)
        || type == typeof(ValueTask)
        || _IsConstructedFrom(type, typeof(ValueTask<>))
        || _IsConstructedFrom(type, typeof(Lazy<>))
        || typeof(Delegate).IsAssignableFrom(type)
        || typeof(Stream).IsAssignableFrom(type)
        || typeof(CancellationTokenSource).IsAssignableFrom(type)
        || typeof(WaitHandle).IsAssignableFrom(type)
        || typeof(Thread).IsAssignableFrom(type)
        || typeof(IServiceProvider).IsAssignableFrom(type)
        || _HasBaseNamed(type)
        || _ImplementsNamed(type)
        || _HoldsLazyLoader(type);

    private static bool _IsConstructedFrom(Type type, Type definition) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == definition;

    private static bool _HasBaseNamed(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.FullName is { } name && _BaseNames.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool _ImplementsNamed(Type type)
    {
        foreach (var contract in type.GetInterfaces())
        {
            if (contract.FullName is { } name && _InterfaceNames.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    // An entity that takes EF Core's lazy loader through its constructor keeps it in a field, and its navigation getters
    // call it: either the ILazyLoader service or the Action<object, string> delegate EF names lazyLoader.
    private static bool _HoldsLazyLoader(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags))
            {
                if (
                    string.Equals(field.FieldType.FullName, _LazyLoaderName, StringComparison.Ordinal)
                    || (
                        field.FieldType == typeof(Action<object, string>)
                        && field.Name.Contains("LazyLoader", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }
}
