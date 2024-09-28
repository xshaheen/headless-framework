// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

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
