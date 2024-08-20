using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.ValueConverters;

[PublicAPI]
public sealed class ExtraPropertiesValueConverter : ValueConverter<ExtraProperties, string>
{
    public ExtraPropertiesValueConverter()
        : base(
            convertToProviderExpression: extraProperties => _Serialize(extraProperties),
            convertFromProviderExpression: json => _Deserialize(json)
        ) { }

    private static string _Serialize(ExtraProperties extraProperties)
    {
        return JsonSerializer.Serialize(extraProperties, PlatformJsonConstants.DefaultInternalJsonOptions);
    }

    private static ExtraProperties _Deserialize(string json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? []
            : JsonSerializer.Deserialize<ExtraProperties>(json, PlatformJsonConstants.DefaultInternalJsonOptions) ?? [];
    }
}

[PublicAPI]
public sealed class ExtraPropertiesValueComparer : ValueComparer<ExtraProperties>
{
    public ExtraPropertiesValueComparer()
        : base(
            equalsExpression: (dictionary1, dictionary2) =>
                (dictionary1 == null && dictionary2 == null)
                || (dictionary1 != null && dictionary2 != null && dictionary1.SequenceEqual(dictionary2)),
            hashCodeExpression: dictionary =>
                dictionary.Aggregate(0, (key, value) => HashCode.Combine(key, value.GetHashCode())),
            snapshotExpression: dictionary => new ExtraProperties(dictionary)
        ) { }
}
