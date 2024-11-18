// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Framework.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public static class ObjectPropertiesHelper
{
    private static readonly ConcurrentDictionary<string, PropertyInfo?> _CachedObjectProperties =
        new(StringComparer.Ordinal);

    public static void TrySetProperty<TObject, TValue>(
        TObject obj,
        Expression<Func<TObject, TValue>> propertySelector,
        Func<TValue> valueFactory,
        params Type[] ignoreAttributeTypes
    )
        where TObject : notnull
    {
        TrySetProperty(obj, propertySelector, _ => valueFactory(), ignoreAttributeTypes);
    }

    public static void TrySetProperty<TObject, TValue>(
        TObject obj,
        Expression<Func<TObject, TValue>> propertySelector,
        Func<TObject, TValue> valueFactory,
        params Type[]? ignoreAttributeTypes
    )
        where TObject : notnull
    {
        var cacheKey =
            $"{obj.GetType().FullName}-"
            + $"{propertySelector}-"
            + $"{(ignoreAttributeTypes is not null ? "-" + string.Join('-', ignoreAttributeTypes.Select(x => x.FullName)) : "")}";

        var property = _CachedObjectProperties.GetOrAdd(
            cacheKey,
            () =>
            {
                if (propertySelector.Body.NodeType is not ExpressionType.MemberAccess)
                {
                    return null;
                }

                var memberExpression = (MemberExpression)propertySelector.Body;

                var propertyInfo = Array.Find(
                    array: obj.GetType().GetProperties(),
                    match: info =>
                        string.Equals(info.Name, memberExpression.Member.Name, StringComparison.Ordinal)
                        && info.GetSetMethod(nonPublic: true) is not null
                );

                if (propertyInfo is null)
                {
                    return null;
                }

                if (
                    ignoreAttributeTypes?.Any(ignoreAttribute => propertyInfo.IsDefined(ignoreAttribute, inherit: true))
                    is true
                )
                {
                    return null;
                }

                return propertyInfo;
            }
        );

        property?.SetValue(obj, valueFactory(obj));
    }
}
