// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

/// <summary>
/// An ordered collection of Name/Value pairs where duplicate names are allowed but aren't typical.
/// Useful for things where a dictionary would work great if not for those pesky edge cases (headers, cookies, etc).
/// </summary>
[PublicAPI]
public sealed class NameValueList<TValue>(bool caseSensitiveNames)
    : List<(string Name, TValue Value)>,
        INameValueList<TValue>,
        IReadOnlyNameValueList<TValue>
{
    /// <summary>
    /// Instantiates a new NameValueList with the Name/Value pairs provided.
    /// </summary>
    public NameValueList(IEnumerable<(string Name, TValue Value)> items, bool caseSensitiveNames)
        : this(caseSensitiveNames)
    {
        AddRange(items);
    }

    /// <inheritdoc />
    public void Add(string name, TValue value)
    {
        Add((name, value));
    }

    /// <inheritdoc />
    public void AddOrReplace(string name, TValue value)
    {
        var i = 0;
        var replaced = false;
        while (i < Count)
        {
            if (!this[i].Name.OrdinalEquals(name, !caseSensitiveNames))
            {
                i++;
            }
            else if (replaced)
            {
                RemoveAt(i);
            }
            else
            {
                this[i] = (name, value);
                replaced = true;
                i++;
            }
        }

        if (!replaced)
        {
            Add(name, value);
        }
    }

    /// <inheritdoc />
    public bool Remove(string name)
    {
        return RemoveAll(x => x.Name.OrdinalEquals(name, !caseSensitiveNames)) > 0;
    }

    /// <inheritdoc />
    public TValue? FirstOrDefault(string name)
    {
        // Indexed scan avoids the Where+Select iterator chain; returns the first match in order.
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Name.OrdinalEquals(name, !caseSensitiveNames))
            {
                return this[i].Value;
            }
        }

        return default;
    }

    /// <inheritdoc />
    public bool TryGetFirst(string name, out TValue? value)
    {
        // Indexed scan avoids allocating the GetAll iterator just to read the first element.
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Name.OrdinalEquals(name, !caseSensitiveNames))
            {
                value = this[i].Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<TValue> GetAll(string name)
    {
        // Indexed yield keeps the original deferred/lazy semantics and order, without the Where+Select chain.
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Name.OrdinalEquals(name, !caseSensitiveNames))
            {
                yield return this[i].Value;
            }
        }
    }

    /// <inheritdoc />
    public bool Contains(string name)
    {
        // Indexed scan avoids the Any iterator allocation.
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Name.OrdinalEquals(name, !caseSensitiveNames))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Contains(string name, TValue? value)
    {
        // Scan honoring caseSensitiveNames. The previous (name, value) tuple Contains used ordinal name
        // equality, ignoring the case-insensitivity flag that every sibling method respects.
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Name.OrdinalEquals(name, !caseSensitiveNames) && Equals(this[i].Value, value))
            {
                return true;
            }
        }

        return false;
    }
}
