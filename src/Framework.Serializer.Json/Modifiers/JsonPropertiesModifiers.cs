// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace Framework.Serializer.Json.Modifiers;

#pragma warning disable CA1000 // Do not declare static members on generic types
public static class JsonPropertiesModifiers<TClass>
    where TClass : class
{
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
