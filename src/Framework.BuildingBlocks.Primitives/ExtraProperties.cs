using Framework.Arguments;

namespace Framework.BuildingBlocks.Primitives;

[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    public ExtraProperties() { }

    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary) { }
}

[PublicAPI]
public interface IHasExtraProperties
{
    ExtraProperties ExtraProperties { get; }
}

#region Extensions

[PublicAPI]
public static class HasExtraPropertiesExtensions
{
    public static bool HasProperty(this IHasExtraProperties source, string name)
    {
        return source.ExtraProperties.ContainsKey(name);
    }

    public static object? GetProperty(this IHasExtraProperties source, string name, object? defaultValue = null)
    {
        return source.ExtraProperties.GetOrDefault(name) ?? defaultValue;
    }

    public static TSource SetProperty<TSource>(this TSource source, string name, object? value)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties[name] = value;

        return source;
    }

    public static TSource RemoveProperty<TSource>(this TSource source, string name)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties.Remove(name);

        return source;
    }

    public static void SetExtraPropertiesToRegularProperties(this IHasExtraProperties source)
    {
        var properties = source
            .GetType()
            .GetProperties()
            .Where(info =>
                source.ExtraProperties.ContainsKey(info.Name) && info.GetSetMethod(nonPublic: true) is not null
            );

        foreach (var property in properties)
        {
            property.SetValue(source, source.ExtraProperties[property.Name]);
            source.RemoveProperty(property.Name);
        }
    }
}

[PublicAPI]
public static class ExtraPropertyExtensions
{
    public static T? ToEnum<T>(this ExtraProperties extraProperties, string key)
        where T : Enum
    {
        var value = extraProperties[key];

        if (value is null)
        {
            return default;
        }

        if (value.GetType() == typeof(T))
        {
            return (T)value;
        }

        var text = value.ToString();

        if (text is null)
        {
            return default;
        }

        extraProperties[key] = Enum.Parse(typeof(T), text, ignoreCase: true);

        return (T)value;
    }

    public static bool HasSameItems(this ExtraProperties dictionary, ExtraProperties otherDictionary)
    {
        Argument.IsNotNull(dictionary);
        Argument.IsNotNull(otherDictionary);

        if (dictionary.Count != otherDictionary.Count)
        {
            return false;
        }

        foreach (var key in dictionary.Keys)
        {
            if (
                !otherDictionary.TryGetValue(key, out var value)
                || !string.Equals(dictionary[key]?.ToString(), value?.ToString(), StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }
}

#endregion
