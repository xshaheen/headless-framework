// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Framework.BuildingBlocks.Helpers.Reflection;
using Framework.Checks;
using Framework.Primitives;

namespace Framework.BuildingBlocks.Models.ExtraProperties;

public static class IncludeExtraPropertiesModifiers
{
    public static void Modify(JsonTypeInfo jsonTypeInfo)
    {
        Argument.IsNotNull(jsonTypeInfo);

        if (!typeof(IHasExtraProperties).IsAssignableFrom(jsonTypeInfo.Type))
        {
            return;
        }

        var propertyJsonInfo = jsonTypeInfo.Properties.FirstOrDefault(x =>
            x.AttributeProvider is MemberInfo memberInfo
            && x.PropertyType == typeof(Framework.Primitives.ExtraProperties)
            && string.Equals(memberInfo.Name, nameof(IHasExtraProperties.ExtraProperties), StringComparison.Ordinal)
            && x.Set is null
        );

        if (propertyJsonInfo is null)
        {
            return;
        }

        propertyJsonInfo.Set = (obj, value) =>
        {
            ObjectPropertiesHelper.TrySetProperty(obj.As<IHasExtraProperties>(), x => x.ExtraProperties, () => value);
        };
    }
}
