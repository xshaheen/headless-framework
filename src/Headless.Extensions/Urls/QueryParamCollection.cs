// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;

namespace Headless.Urls;

/// <summary>
/// Represents a URL query as a collection of name/value pairs. Insertion order is preserved.
/// </summary>
[PublicAPI]
public sealed class QueryParamCollection : IReadOnlyNameValueList<object?>
{
    private readonly NameValueList<QueryParamValue> _values = new(caseSensitiveNames: true);

    /// <summary>
    /// Returns a new instance of QueryParamCollection
    /// </summary>
    /// <param name="query">Optional query string to parse.</param>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "ToKeyValuePairs only uses string parsing path here, no reflection involved."
    )]
    public QueryParamCollection(string? query = null)
    {
        if (query is null)
        {
            return;
        }

        _values.AddRange(
            from kv in query.TrimStart('?').ToKeyValuePairs()
            select (kv.Key, new QueryParamValue(kv.Value, isEncoded: true))
        );
    }

    /// <summary>
    /// Creates a copy of this <see cref="QueryParamCollection"/>, preserving insertion order and each
    /// parameter's original encoding state. Faster than rendering to a query string and re-parsing it.
    /// </summary>
    public QueryParamCollection Clone()
    {
        var clone = new QueryParamCollection();
        clone._values.AddRange(_values);
        return clone;
    }

    /// <summary>
    /// Returns serialized, encoded query string. Insertion order is preserved.
    /// </summary>
    public override string ToString() => ToString(encodeSpaceAsPlus: false);

    /// <summary>
    /// Returns serialized, encoded query string. Insertion order is preserved.
    /// </summary>
    public string ToString(bool encodeSpaceAsPlus)
    {
        if (_values.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        AppendTo(sb, encodeSpaceAsPlus);
        return sb.ToString();
    }

    /// <summary>
    /// Appends serialized, encoded query string to the provided StringBuilder.
    /// </summary>
    /// <param name="sb">The StringBuilder to append to.</param>
    /// <param name="encodeSpaceAsPlus">If true, encode spaces as + instead of %20.</param>
    public void AppendTo(StringBuilder sb, bool encodeSpaceAsPlus)
    {
        var first = true;
        foreach (var (parameterName, parameterValue) in _values)
        {
            if (!first)
            {
                sb.Append('&');
            }

            first = false;

            var name = Url.EncodeIllegalCharacters(parameterName, encodeSpaceAsPlus);
            var value = parameterValue.Encode(encodeSpaceAsPlus);

            sb.Append(name);
            if (value is not null)
            {
                sb.Append('=');
                sb.Append(value);
            }
        }
    }

    /// <summary>
    /// Appends a query parameter. If value is a collection type (array, IEnumerable, etc.), multiple parameters are added, i.e. x=1&amp;x=2.
    /// To overwrite existing parameters of the same name, use AddOrReplace instead.
    /// </summary>
    /// <param name="name">Name of the parameter.</param>
    /// <param name="value">Value of the parameter. If it's a collection, multiple parameters of the same name are added.</param>
    /// <param name="isEncoded">If true, assume value(s) already URL-encoded.</param>
    /// <param name="nullValueHandling">Describes how to handle null values.</param>
    public void Add(
        string name,
        object? value,
        bool isEncoded = false,
        NullValueHandling nullValueHandling = NullValueHandling.Remove
    )
    {
        if (value is null && nullValueHandling == NullValueHandling.Remove)
        {
            _values.Remove(name);
            return;
        }

        foreach (var val in _SplitCollection(value))
        {
            if (val is null && nullValueHandling != NullValueHandling.NameOnly)
            {
                continue;
            }

            _values.Add(name, new QueryParamValue(val, isEncoded));
        }
    }

    /// <summary>
    /// Replaces existing query parameter(s) or appends to the end. If value is a collection type (array, IEnumerable, etc.),
    /// multiple parameters are added, i.e. x=1&amp;x=2. If any of the same name already exist, they are overwritten one by one
    /// (preserving order) and any remaining are appended to the end. If fewer values are specified than already exist,
    /// remaining existing values are removed.
    /// </summary>
    /// <param name="name">Name of the parameter.</param>
    /// <param name="value">Value of the parameter. If it's a collection, multiple parameters of the same name are added/replaced.</param>
    /// <param name="isEncoded">If true, assume value(s) already URL-encoded.</param>
    /// <param name="nullValueHandling">Describes how to handle null values.</param>
    public void AddOrReplace(
        string name,
        object? value,
        bool isEncoded = false,
        NullValueHandling nullValueHandling = NullValueHandling.Remove
    )
    {
        if (!Contains(name))
        {
            // Simple append: no existing values to replace in place, so skip the ToArray/Clear/rebuild below
            // (which is O(n) per call and allocates the backing array on every add).
            Add(name, value, isEncoded, nullValueHandling);
            return;
        }

        // This covers some complex edge cases involving multiple values of the same name.
        // example: x has values at positions 2 and 4 in the query string, then we set x to
        // an array of 4 values. We want to replace the values at positions 2 and 4 with the
        // first 2 values of the new array, then append the remaining 2 values to the end.
        var values = new Queue<object?>(_SplitCollection(value));

        var old = _values.ToArray();
        _values.Clear();

        foreach (var item in old)
        {
            if (!string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                _values.Add(item);
                continue;
            }

            if (values.Count == 0)
            {
                continue; // remove, effectively
            }

            var val = values.Dequeue();
            if (val is null && nullValueHandling == NullValueHandling.Ignore)
            {
                _values.Add(item);
            }
            else if (val is not null || nullValueHandling != NullValueHandling.Remove)
            {
                Add(name, val, isEncoded, nullValueHandling);
            }
        }

        // add the rest to the end
        while (values.Count > 0)
        {
            Add(name, values.Dequeue(), isEncoded, nullValueHandling);
        }
    }

    // Cached method-group delegate so the recursive SelectMany call does not allocate a new
    // Func<object?, IEnumerable<object?>> on every level of nesting.
    private static readonly Func<object?, IEnumerable<object?>> _SplitCollectionDelegate = _SplitCollection;

    private static IEnumerable<object?> _SplitCollection(object? value)
    {
        // Scalar fast-path: null, string, and any non-IEnumerable value are not split, so yield
        // them directly. This mirrors the original branch order (null/string before IEnumerable)
        // and only the non-null, non-string IEnumerable case is recursively split below.
        switch (value)
        {
            case IEnumerable en and not string:
                foreach (var item in en.Cast<object?>().SelectMany(_SplitCollectionDelegate))
                {
                    yield return item;
                }

                break;

            default:
                yield return value;
                break;
        }
    }

    /// <summary>
    /// Removes all query parameters of the given name.
    /// </summary>
    public void Remove(string name) => _values.Remove(name);

    /// <summary>
    /// Clears all query parameters from this collection.
    /// </summary>
    public void Clear() => _values.Clear();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IEnumerator<(string Name, object? Value)> GetEnumerator()
    {
        // Indexed loop avoids allocating the Select iterator + closure on every enumeration.
        for (var i = 0; i < _values.Count; i++)
        {
            var (name, value) = _values[i];
            yield return (name, value.Value);
        }
    }

    /// <inheritdoc />
    public int Count => _values.Count;

    /// <inheritdoc />
    public (string Name, object? Value) this[int index] => (_values[index].Name, _values[index].Value.Value);

    /// <inheritdoc />
    public object? FirstOrDefault(string name) => _values.FirstOrDefault(name).Value;

    /// <inheritdoc />
    public bool TryGetFirst(string name, out object? value)
    {
        var result = _values.TryGetFirst(name, out var qv);
        value = qv.Value;
        return result;
    }

    /// <inheritdoc />
    public IEnumerable<object?> GetAll(string name)
    {
        // Single indexed iterator mirroring NameValueList.GetAll (case-sensitive names here), instead of
        // chaining GetAll(...) with a Select projection.
        for (var i = 0; i < _values.Count; i++)
        {
            var (parameterName, parameterValue) = _values[i];
            if (parameterName.OrdinalEquals(name))
            {
                yield return parameterValue.Value;
            }
        }
    }

    /// <inheritdoc />
    public bool Contains(string name) => _values.Contains(name);

    /// <inheritdoc />
    public bool Contains(string name, object? value)
    {
        // Indexed scan avoids the Any closure; compares the unwrapped value, not the QueryParamValue wrapper.
        for (var i = 0; i < _values.Count; i++)
        {
            var (parameterName, parameterValue) = _values[i];
            if (string.Equals(parameterName, name, StringComparison.Ordinal) && Equals(parameterValue.Value, value))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Represents a query parameter value with the ability to track whether it was already encoded when created.
/// </summary>
internal readonly struct QueryParamValue
{
    private readonly string? _encodedValue;

    public QueryParamValue(object? value, bool isEncoded)
    {
        if (isEncoded && value is string s)
        {
            _encodedValue = s;
            Value = Url.Decode(s, interpretPlusAsSpace: true);
        }
        else
        {
            Value = value;
            _encodedValue = null;
        }
    }

    public object? Value { get; }

    public string? Encode(bool encodeSpaceAsPlus)
    {
        return Value is null
            ? null
            : _encodedValue
                ?? (
                    Value is string s
                        ? Url.Encode(s, encodeSpaceAsPlus)
                        : Url.Encode(Value.ToInvariantString(), encodeSpaceAsPlus)
                );
    }
}
