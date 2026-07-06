// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Headless.Reflection;

/// <summary>
/// Helpers for setting object properties by reflection, including non-public setters, with a per-type cache of the
/// resolved <see cref="PropertyInfo"/> entries.
/// </summary>
[PublicAPI]
public static class ObjectPropertiesHelper
{
    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<PropertyCacheKey, CachedResult<CachedProperty>>
    > _Cache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<PropertyCacheKey, CachedResult<CachedProperty>>
    >.CreateValueCallback _CreateInner = static _ => new ConcurrentDictionary<
        PropertyCacheKey,
        CachedResult<CachedProperty>
    >();

    /// <summary>
    /// Sets the property selected by <paramref name="propertySelector"/> to the value produced by
    /// <paramref name="valueFactory"/>, if the property exists and is writable (including non-public setters) and is
    /// not decorated with any of <paramref name="ignoreAttributeTypes"/>.
    /// </summary>
    /// <typeparam name="TObject">The type of the object whose property is set.</typeparam>
    /// <typeparam name="TValue">The type of the property value.</typeparam>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="propertySelector">An expression selecting the target property, for example <c>x =&gt; x.Name</c>.</param>
    /// <param name="valueFactory">Factory producing the value to assign.</param>
    /// <param name="ignoreAttributeTypes">Attribute types that, if applied to the property, cause the set to be skipped.</param>
    /// <returns><see langword="true"/> if the property was found and set; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when the property's setter throws.</exception>
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

    /// <summary>
    /// Sets the property selected by <paramref name="propertySelector"/> to the value produced by
    /// <paramref name="valueFactory"/> (which receives <paramref name="obj"/>), if the property exists and is writable
    /// (including non-public setters) and is not decorated with any of <paramref name="ignoreAttributeTypes"/>.
    /// </summary>
    /// <typeparam name="TObject">The type of the object whose property is set.</typeparam>
    /// <typeparam name="TValue">The type of the property value.</typeparam>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="propertySelector">An expression selecting the target property, for example <c>x =&gt; x.Name</c>.</param>
    /// <param name="valueFactory">Factory producing the value to assign, given the target object.</param>
    /// <param name="ignoreAttributeTypes">Attribute types that, if applied to the property, cause the set to be skipped.</param>
    /// <returns><see langword="true"/> if the property was found and set; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when the property's setter throws.</exception>
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

    /// <summary>
    /// Sets the named property to the value produced by <paramref name="valueFactory"/>, if the property exists and
    /// is writable (including non-public setters) and is not decorated with any of
    /// <paramref name="ignoreAttributeTypes"/>. Prefer this overload on hot paths: unlike the expression-selector
    /// overloads it builds no expression tree per call.
    /// </summary>
    /// <typeparam name="TObject">The type of the object whose property is set.</typeparam>
    /// <typeparam name="TValue">The type of the property value.</typeparam>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="propertyName">The exact (case-sensitive) name of the property to set, for example <c>nameof(X.Name)</c>.</param>
    /// <param name="valueFactory">Factory producing the value to assign.</param>
    /// <param name="ignoreAttributeTypes">Attribute types that, if applied to the property, cause the set to be skipped.</param>
    /// <returns><see langword="true"/> if the property was found and set; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when the property's setter throws.</exception>
    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    public static bool TrySetProperty<TObject, TValue>(
        TObject obj,
        string propertyName,
        Func<TValue> valueFactory,
        params Type[]? ignoreAttributeTypes
    )
        where TObject : notnull
    {
        var property = _GetCachedProperty(obj.GetType(), propertyName, ignoreAttributeTypes);

        if (property is null)
        {
            return false;
        }

        property.SetValue(obj, valueFactory());

        return true;
    }

    /// <summary>
    /// Sets the named property to <see langword="null"/>, if the property exists, is writable (including non-public
    /// setters), is of a nullable type, and is not decorated with any of <paramref name="ignoreAttributeTypes"/>.
    /// </summary>
    /// <typeparam name="TObject">The type of the object whose property is set.</typeparam>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="propertyName">The exact (case-sensitive) name of the property to set.</param>
    /// <param name="ignoreAttributeTypes">Attribute types that, if applied to the property, cause the set to be skipped.</param>
    /// <returns><see langword="true"/> if the property was found, is nullable, and was set to <see langword="null"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when the property's setter throws.</exception>
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

    /// <summary>
    /// Sets the named property to the value produced by <paramref name="valueFactory"/> (which receives
    /// <paramref name="obj"/>), if the property exists and is writable (including non-public setters) and is not
    /// decorated with any of <paramref name="ignoreAttributeTypes"/>. Prefer this overload on hot paths: unlike the
    /// expression-selector overloads it builds no expression tree per call.
    /// </summary>
    /// <typeparam name="TObject">The type of the object whose property is set.</typeparam>
    /// <typeparam name="TValue">The type of the property value.</typeparam>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="propertyName">The exact (case-sensitive) name of the property to set, for example <c>nameof(X.Name)</c>.</param>
    /// <param name="valueFactory">Factory producing the value to assign, given the target object.</param>
    /// <param name="ignoreAttributeTypes">Attribute types that, if applied to the property, cause the set to be skipped.</param>
    /// <returns><see langword="true"/> if the property was found and set; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when the property's setter throws.</exception>
    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    public static bool TrySetProperty<TObject, TValue>(
        TObject obj,
        string propertyName,
        Func<TObject, TValue> valueFactory,
        params Type[]? ignoreAttributeTypes
    )
        where TObject : notnull
    {
        var property = _GetCachedProperty(obj.GetType(), propertyName, ignoreAttributeTypes);

        if (property is null)
        {
            return false;
        }

        property.SetValue(obj, valueFactory(obj));

        return true;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static CachedProperty? _GetCachedProperty(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type objType,
        string propertyName,
        Type[]? ignoreAttrs
    )
    {
        var inner = _Cache.GetValue(objType, _CreateInner);
        var normalizedAttrs = ignoreAttrs is { Length: 0 } ? null : ignoreAttrs;
        var key = new PropertyCacheKey(propertyName, normalizedAttrs);

        if (inner.TryGetValue(key, out var existing))
        {
            return existing.Value;
        }

        var propertyInfo = _GetWritablePropertyInfo(objType, propertyName, normalizedAttrs);
        var result = new CachedResult<CachedProperty>(propertyInfo is null ? null : new CachedProperty(propertyInfo));

        return inner.GetOrAdd(key, result).Value;
    }

    /// <summary>
    /// A resolved writable property plus a lazily compiled open setter delegate: assignment through the compiled
    /// delegate is roughly an order of magnitude cheaper than <see cref="PropertyInfo.SetValue(object?, object?)"/>,
    /// which matters because the ORM save pipeline stamps several properties per entity per save.
    /// </summary>
    private sealed class CachedProperty(PropertyInfo property)
    {
        // The benign publication race compiles the same delegate twice at worst.
        private Action<object, object?>? _compiledSetter;

        public PropertyInfo Property { get; } = property;

        public Type PropertyType => Property.PropertyType;

        public void SetValue(object obj, object? value)
        {
            // Value-type receivers keep reflection SetValue (a compiled unbox-assign would mutate a copy),
            // and static setters keep it too (Expression.Call rejects an instance expression for them).
            if (Property.DeclaringType?.IsValueType != false || Property.SetMethod?.IsStatic != false)
            {
                Property.SetValue(obj, value);

                return;
            }

            var setter = _compiledSetter ??= _CompileSetter(Property);

            try
            {
                setter(obj, value);
            }
            catch (Exception e)
            {
                // Preserve the documented PropertyInfo.SetValue contract: setter faults surface wrapped.
                throw new TargetInvocationException(e);
            }
        }

        private static Action<object, object?> _CompileSetter(PropertyInfo property)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var setMethod = property.GetSetMethod(nonPublic: true)!;

            var body = Expression.Call(
                Expression.Convert(objParam, property.DeclaringType!),
                setMethod,
                Expression.Convert(valueParam, property.PropertyType)
            );

            return Expression.Lambda<Action<object, object?>>(body, objParam, valueParam).Compile();
        }
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
