// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>
/// A <c>System.Text.Json</c> type-info modifier that makes the otherwise read-only
/// <see cref="IHasExtraProperties.ExtraProperties"/> property settable during deserialization.
/// </summary>
[PublicAPI]
public static class IncludeExtraPropertiesModifiers
{
    /// <summary>
    /// Wires up a setter for the <see cref="IHasExtraProperties.ExtraProperties"/> property on
    /// <paramref name="jsonTypeInfo"/>. Has no effect when the type does not implement
    /// <see cref="IHasExtraProperties"/> or has no matching read-only extra-properties property.
    /// </summary>
    /// <param name="jsonTypeInfo">The JSON type-info contract to modify.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonTypeInfo"/> is <see langword="null"/>.</exception>
    public static void Modify(JsonTypeInfo jsonTypeInfo)
    {
        Argument.IsNotNull(jsonTypeInfo);

        if (!typeof(IHasExtraProperties).IsAssignableFrom(jsonTypeInfo.Type))
        {
            return;
        }

        var propertyJsonInfo = jsonTypeInfo.Properties.FirstOrDefault(x =>
            x.AttributeProvider is MemberInfo memberInfo
            && x.PropertyType == typeof(ExtraProperties)
            && string.Equals(memberInfo.Name, nameof(IHasExtraProperties.ExtraProperties), StringComparison.Ordinal)
            && x.Set is null
        );

        // Only wire a setter when the CLR property actually has one (including a non-public setter). This is the
        // read-only-property-during-deserialization case the modifier exists to unblock.
        if (
            propertyJsonInfo?.AttributeProvider is not PropertyInfo clrProperty
            || clrProperty.GetSetMethod(nonPublic: true) is null
        )
        {
            return;
        }

        propertyJsonInfo.Set = (obj, value) => clrProperty.SetValue(obj, value);
    }
}
