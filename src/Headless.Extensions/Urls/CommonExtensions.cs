using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Urls;

/// <summary>
/// CommonExtensions for objects.
/// </summary>
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
            IEnumerable e => _CollectionToKV(e),
            _ => _ObjectToKV(obj),
        };
    }

    internal static bool OrdinalEquals(this string? s, string? value, bool ignoreCase = false) =>
        s is not null && s.Equals(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool OrdinalContains(this string? s, string value, bool ignoreCase = false) =>
        s is not null && s.Contains(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool OrdinalStartsWith(this string? s, string value, bool ignoreCase = false) =>
        s is not null
        && s.StartsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static bool OrdinalEndsWith(this string? s, string value, bool ignoreCase = false) =>
        s is not null && s.EndsWith(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Splits at the first occurrence of the given separator.
    /// </summary>
    /// <param name="s">The string to split.</param>
    /// <param name="separator">The separator to split on.</param>
    /// <returns>Array of at most 2 strings. (1 if separator is not found.)</returns>
    public static string[] SplitOnFirstOccurence(this string s, string separator)
    {
        if (string.IsNullOrEmpty(s))
        {
            return [s];
        }

        var i = s.IndexOf(separator, StringComparison.Ordinal);
        return i == -1 ? [s] : [s[..i], s[(i + separator.Length)..]];
    }

    private static IEnumerable<(string Key, object? Value)> _StringToKV(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return [];
        }

        return from p in s.Split('&')
            let pair = p.SplitOnFirstOccurence("=")
            let name = pair[0]
            let value = pair.Length == 1 ? null : pair[1]
            select (name, (object?)value);
    }

    [RequiresUnreferencedCode("Uses Type.GetProperties which is not compatible with trimming.")]
    private static IEnumerable<(string Name, object? Value)> _ObjectToKV(object obj) =>
        from prop in obj.GetType().GetProperties()
        let getter = prop.GetGetMethod(false)
        where getter is not null
        let val = getter.Invoke(obj, null)
        select (prop.Name, _GetDeclaredTypeValue(val, prop.PropertyType));

    [RequiresUnreferencedCode("Uses Type.GetInterfaces which is not compatible with trimming.")]
    internal static object? _GetDeclaredTypeValue(object? value, Type declaredType)
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
            value = prop.GetValue(obj, null);
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
