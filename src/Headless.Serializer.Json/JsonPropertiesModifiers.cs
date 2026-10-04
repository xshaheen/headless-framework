// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace Headless.Serializer.Modifiers;

/// <summary>
/// Factory for <c>System.Text.Json</c> type-info modifier delegates that adjust property visibility for
/// <typeparamref name="TClass"/> at serialization contract build time.
/// </summary>
/// <remarks>
/// The returned <see cref="Action{T}"/> delegates are designed to be added to
/// <see cref="System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.Modifiers"/> (or an equivalent
/// resolver) so that property adjustments are applied once during options warm-up rather than on every
/// serialize/deserialize call.
/// </remarks>
/// <typeparam name="TClass">The class whose serialization contract is being modified.</typeparam>
#pragma warning disable CA1000 // Do not declare static members on generic types
public static class JsonPropertiesModifiers<TClass>
    where TClass : class
{
    /// <summary>
    /// Creates a type-info modifier that removes the property selected by <paramref name="propertySelector"/>
    /// from the JSON contract of <typeparamref name="TClass"/>, causing it to be neither serialized nor
    /// deserialized.
    /// </summary>
    /// <typeparam name="TProperty">The property type (inferred).</typeparam>
    /// <param name="propertySelector">A strongly-typed expression identifying the property to ignore.</param>
    /// <returns>
    /// A modifier delegate suitable for <see cref="System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.Modifiers"/>.
    /// </returns>
    public static Action<JsonTypeInfo> CreateIgnorePropertyModifyAction<TProperty>(
        Expression<Func<TClass, TProperty>> propertySelector
    )
    {
        return jsonTypeInfo =>
        {
            if (jsonTypeInfo.Type != typeof(TClass))
            {
                return;
            }

            _RemoveAll(
                jsonTypeInfo.Properties,
                x =>
                    x.AttributeProvider is MemberInfo memberInfo
                    && string.Equals(
                        memberInfo.Name,
                        ((MemberExpression)propertySelector.Body).Member.Name,
                        StringComparison.Ordinal
                    )
            );
        };
    }

    /// <summary>
    /// Creates a type-info modifier that enables deserialization of a property that has a non-public setter,
    /// by locating the setter via reflection and injecting it into the JSON contract for <typeparamref name="TClass"/>.
    /// </summary>
    /// <remarks>
    /// The modifier is a no-op when the property already has a public setter, or when the property cannot
    /// be found via reflection.
    /// </remarks>
    /// <typeparam name="TProperty">The property type (inferred).</typeparam>
    /// <param name="propertySelector">A strongly-typed expression identifying the property whose setter to expose.</param>
    /// <returns>
    /// A modifier delegate suitable for <see cref="System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.Modifiers"/>.
    /// </returns>
    public static Action<JsonTypeInfo> CreateIncludeNonPublicPropertiesModifyAction<TProperty>(
        Expression<Func<TClass, TProperty>> propertySelector
    )
    {
        return jsonTypeInfo =>
        {
            if (jsonTypeInfo.Type != typeof(TClass))
            {
                return;
            }

            var propertyName = ((MemberExpression)propertySelector.Body).Member.Name;

            var propertyJsonInfo = jsonTypeInfo.Properties.FirstOrDefault(x =>
                x.AttributeProvider is MemberInfo memberInfo
                && string.Equals(memberInfo.Name, propertyName, StringComparison.Ordinal)
                && x.Set == null
            );

            if (propertyJsonInfo is null)
            {
                return;
            }

            var propertyInfo = typeof(TClass).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (propertyInfo is not null)
            {
                propertyJsonInfo.Set = propertyInfo.SetValue;
            }
        };
    }

    private static void _RemoveAll<T>(ICollection<T> source, Func<T, bool> predicate)
    {
        var itemsToBeRemoved = source.Where(predicate).ToList();

        foreach (var item in itemsToBeRemoved)
        {
            source.Remove(item);
        }
    }
}
