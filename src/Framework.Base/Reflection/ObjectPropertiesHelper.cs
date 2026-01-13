// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Framework.Reflection;

[PublicAPI]
public static class ObjectPropertiesHelper
{
    private static readonly ConcurrentDictionary<string, PropertyInfo?> _CachedObjectProperties = new(
        StringComparer.Ordinal
    );

    public static bool TrySetProperty<TObject, TValue>(
        TObject obj,
        Expression<Func<TObject, TValue>> propertySelector,
        Func<TValue> valueFactory,
        params Type[] ignoreAttributeTypes
    )
        where TObject : notnull
    {
        return TrySetProperty(obj, propertySelector, _ => valueFactory(), ignoreAttributeTypes);
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    public static bool TrySetProperty<TObject, TValue>(
        TObject obj,
        Expression<Func<TObject, TValue>> propertySelector,
        Func<TObject, TValue> valueFactory,
        params Type[]? ignoreAttributeTypes
    )
        where TObject : notnull
    {
        var objType = obj.GetType();
        var cacheKey = _GetCacheKey(objType, propertySelector.ToString(), ignoreAttributeTypes);

        var property = _CachedObjectProperties.GetOrAdd(
            cacheKey,
            valueFactory: static (_, args) =>
            {
                var (objType, propertySelector, ignoreAttributeTypes) = args;
                var propertyName = _GetPropertyName(propertySelector);
                return _GetWritablePropertyInfo(objType, propertyName, ignoreAttributeTypes);
            },
            factoryArgument: (objType, propertySelector, ignoreAttributeTypes)
        );

        if (property is null)
        {
            return false;
        }

        property.SetValue(obj, valueFactory(obj));

        return true;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    public static bool TrySetPropertyToNull<TObject>(
        TObject obj,
        string propertyName,
        params Type[] ignoreAttributeTypes
    )
        where TObject : notnull
    {
        var objType = obj.GetType();
        var cacheKey = _GetCacheKey(objType, "x => x." + propertyName, ignoreAttributeTypes);

        var property = _CachedObjectProperties.GetOrAdd(
            cacheKey,
            static (_, args) =>
            {
                var (objType, propertyName, ignoreAttributeTypes) = args;
                return _GetWritablePropertyInfo(objType, propertyName, ignoreAttributeTypes);
            },
            factoryArgument: (objType, propertyName, ignoreAttributeTypes)
        );

        if (property?.PropertyType.IsNullableType() is true)
        {
            property.SetValue(obj, value: null);

            return true;
        }

        return false;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static PropertyInfo? _GetWritablePropertyInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type objType,
        string? propertyName,
        Type[]? ignoreAttr)
    {
        if (propertyName is null)
        {
            return null;
        }

        var propertyInfo = objType
            .GetProperties()
            .FirstOrDefault(x => string.Equals(x.Name, propertyName, StringComparison.Ordinal));

        if (propertyInfo?.CanWrite is not true)
        {
            return null;
        }

        if (ignoreAttr?.Any(attr => propertyInfo.IsDefined(attr, inherit: true)) is true)
        {
            return null;
        }

        var setMethod = propertyInfo.GetSetMethod(nonPublic: true);

        if (setMethod is null)
        {
            return null;
        }

        return propertyInfo;
    }

    private static string? _GetPropertyName(LambdaExpression propertySelector)
    {
        var memberExpression = propertySelector.Body.NodeType switch
        {
            ExpressionType.Convert => propertySelector.Body.As<UnaryExpression>()?.Operand.As<MemberExpression>(),
            ExpressionType.MemberAccess => propertySelector.Body.As<MemberExpression>(),
            _ => null,
        };

        return memberExpression?.Member.Name;
    }

    private static string _GetCacheKey(Type objType, string propertySelector, Type[]? attr)
    {
        var attrKey = attr is null ? "" : "-" + string.Join('-', attr.Select(x => x.FullName));

        return $"{objType.FullName}-{propertySelector}{attrKey}";
    }
}
