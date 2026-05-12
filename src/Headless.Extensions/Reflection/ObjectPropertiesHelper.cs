// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Headless.Reflection;

[PublicAPI]
public static class ObjectPropertiesHelper
{
    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<PropertyCacheKey, CachedResult<PropertyInfo>>
    > _Cache = new();

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<PropertyCacheKey, CachedResult<PropertyInfo>>
    >.CreateValueCallback _CreateInner = static _ => new ConcurrentDictionary<
        PropertyCacheKey,
        CachedResult<PropertyInfo>
    >();

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
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
        var propertyName = _GetPropertyName(propertySelector);

        if (propertyName is null)
        {
            return false;
        }

        var property = _GetCachedProperty(obj.GetType(), propertyName, ignoreAttributeTypes);

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
        var property = _GetCachedProperty(obj.GetType(), propertyName, ignoreAttributeTypes);

        if (property?.PropertyType.IsNullableType() is true)
        {
            property.SetValue(obj, value: null);

            return true;
        }

        return false;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static PropertyInfo? _GetCachedProperty(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type objType,
        string propertyName,
        Type[]? ignoreAttrs
    )
    {
        var inner = _Cache.GetValue(objType, _CreateInner);
        var key = new PropertyCacheKey(propertyName, ignoreAttrs);

        if (inner.TryGetValue(key, out var existing))
        {
            return existing.Value;
        }

        var result = new CachedResult<PropertyInfo>(_GetWritablePropertyInfo(objType, propertyName, ignoreAttrs));

        return inner.GetOrAdd(key, result).Value;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static PropertyInfo? _GetWritablePropertyInfo(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type objType,
        string propertyName,
        Type[]? ignoreAttr
    )
    {
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

    private readonly record struct PropertyCacheKey(string PropertyName, Type[]? IgnoreAttrs)
    {
        public bool Equals(PropertyCacheKey other)
        {
            if (!string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal))
            {
                return false;
            }

            if (ReferenceEquals(IgnoreAttrs, other.IgnoreAttrs))
            {
                return true;
            }

            if (IgnoreAttrs is null || other.IgnoreAttrs is null)
            {
                return false;
            }

            if (IgnoreAttrs.Length != other.IgnoreAttrs.Length)
            {
                return false;
            }

            for (var i = 0; i < IgnoreAttrs.Length; i++)
            {
                if (IgnoreAttrs[i] != other.IgnoreAttrs[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(PropertyName, StringComparer.Ordinal);

            if (IgnoreAttrs is not null)
            {
                foreach (var t in IgnoreAttrs)
                {
                    hash.Add(t);
                }
            }

            return hash.ToHashCode();
        }
    }
}
