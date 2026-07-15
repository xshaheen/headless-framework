// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Checks;

namespace Headless.Urls;

/// <summary>
/// Extension methods used by the URL builder for converting objects to query name/value pairs and for ordinal
/// string comparisons.
/// </summary>
[PublicAPI]
public static class CommonExtensions
{
    /// <summary>
    /// Returns a key-value-pairs representation of the object.
    /// For strings, URL query string format assumed and pairs are parsed from that.
    /// For objects that already implement IEnumerable&lt;KeyValuePair&gt;, the object itself is simply returned.
    /// For all other objects, all publicly readable properties are extracted and returned as pairs.
    /// </summary>
    /// <param name="obj">The object to parse into key-value pairs</param>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is <see langword="null" />.</exception>
    [RequiresUnreferencedCode("Uses Type.GetProperties and Type.GetField which is not compatible with trimming.")]
    public static IEnumerable<(string Key, object? Value)> ToKeyValuePairs(this object obj)
    {
        Argument.IsNotNull(obj);

        return obj switch
        {
            string s => _StringToKV(s),
            // Typed fast paths: dictionaries and KeyValuePair sequences expose Key/Value statically,
            // so read them directly instead of reflecting over each element in _CollectionToKV.
            IEnumerable<KeyValuePair<string, object?>> kv => kv.Select(p => (p.Key, p.Value)),
            IEnumerable<KeyValuePair<string, string>> kv => kv.Select(p => (p.Key, (object?)p.Value)),
            IEnumerable e => _CollectionToKV(e),
            _ => _ObjectToKV(obj),
        };
    }

    internal static bool OrdinalEquals(this string? s, string? value, bool ignoreCase = false)
    {
        return s?.Equals(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
    }

    internal static bool OrdinalContains(this string? s, string value, bool ignoreCase = false)
    {
        return s?.Contains(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
    }

    internal static bool OrdinalStartsWith(this string? s, string value, bool ignoreCase = false)
    {
        return s?.StartsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
    }

    internal static bool OrdinalEndsWith(this string? s, string value, bool ignoreCase = false)
    {
        return s?.EndsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Splits at the first occurrence of the given separator.
    /// </summary>
    /// <param name="s">The string to split.</param>
    /// <param name="separator">The separator to split on.</param>
    /// <returns>Array of at most 2 strings. (1 if separator is not found.)</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="separator"/> is <see langword="null"/> and <paramref name="s"/> is neither <see langword="null"/> nor empty.</exception>
    internal static string[] SplitOnFirstOccurrence(this string s, string separator)
    {
        if (string.IsNullOrEmpty(s))
        {
            return [s];
        }

        var i = s.IndexOf(separator, StringComparison.Ordinal);
        return i == -1 ? [s] : [s[..i], s[(i + separator.Length)..]];
    }

    private static List<(string Key, object? Value)> _StringToKV(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return [];
        }

        var pairs = s.Split('&');
        var result = new List<(string Key, object? Value)>(pairs.Length);
        foreach (var pair in pairs)
        {
            // Split on the first '='. No '=' yields a null value (key-only); an empty right side yields "".
            var i = pair.AsSpan().IndexOf('=');
            if (i < 0)
            {
                result.Add((pair, null));
            }
            else
            {
                result.Add((pair[..i], pair[(i + 1)..]));
            }
        }

        return result;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static IEnumerable<(string Name, object? Value)> _ObjectToKV(object obj)
    {
        return from prop in obj.GetType().GetProperties()
            let getter = prop.GetGetMethod(nonPublic: false)
            where getter is not null
            let val = getter.Invoke(obj, parameters: null)
            select (prop.Name, GetDeclaredTypeValue(val, prop.PropertyType));
    }

    [RequiresUnreferencedCode("Uses Type.GetInterfaces which is not compatible with trimming.")]
    internal static object? GetDeclaredTypeValue(object? value, Type declaredType)
    {
        if (value is null || value.GetType() == declaredType)
        {
            return value;
        }

        // without this we had https://github.com/tmenier/Flurl/issues/669
        // related: https://stackoverflow.com/q/3531318/62600
        declaredType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        // added to deal with https://github.com/tmenier/Flurl/issues/632
        // thx @j2jensen!
        if (
            value is IEnumerable col
            && declaredType.IsGenericType
            && declaredType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            && !col.GetType().GetInterfaces().Contains(declaredType)
            && declaredType.IsInstanceOfType(col)
        )
        {
            var elementType = declaredType.GetGenericArguments()[0];
            return col.Cast<object>()
                .Select(element => Convert.ChangeType(element, elementType, CultureInfo.InvariantCulture));
        }

        return value;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperty and Type.GetField which is not compatible with trimming.")]
    private static IEnumerable<(string Key, object? Value)> _CollectionToKV(IEnumerable col)
    {
        foreach (var item in col)
        {
            if (item is null)
            {
                continue;
            }

            if (!_IsTuple2(item, out var name, out var val) && !_LooksLikeKV(item, out name, out val))
            {
                yield return (item.ToInvariantString(), null);
            }
            else if (name is not null)
            {
                yield return (name.ToInvariantString(), val);
            }
        }
    }

    [RequiresUnreferencedCode("Uses Type.GetProperty and Type.GetField which is not compatible with trimming.")]
    private static bool _TryGetProp(object obj, string name, out object? value)
    {
        var prop = obj.GetType().GetProperty(name);
        var field = obj.GetType().GetField(name);

        if (prop is not null)
        {
            value = prop.GetValue(obj, index: null);
            return true;
        }

        if (field is not null)
        {
            value = field.GetValue(obj);
            return true;
        }

        value = null;
        return false;
    }

    [RequiresUnreferencedCode("Uses Type.GetProperty and Type.GetField which is not compatible with trimming.")]
    private static bool _IsTuple2(object item, out object? name, out object? val)
    {
        name = null;
        val = null;
        return item.GetType().Name.OrdinalContains("Tuple")
            && _TryGetProp(item, "Item1", out name)
            && _TryGetProp(item, "Item2", out val)
            && !_TryGetProp(item, "Item3", out _);
    }

    [RequiresUnreferencedCode("Uses Type.GetProperty and Type.GetField which is not compatible with trimming.")]
    private static bool _LooksLikeKV(object item, out object? name, out object? val)
    {
        name = null;
        val = null;
        return (
                _TryGetProp(item, "Key", out name)
                || _TryGetProp(item, "key", out name)
                || _TryGetProp(item, "Name", out name)
                || _TryGetProp(item, "name", out name)
            ) && (_TryGetProp(item, "Value", out val) || _TryGetProp(item, "value", out val));
    }
}
