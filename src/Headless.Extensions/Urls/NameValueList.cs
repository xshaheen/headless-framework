// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

/// <summary>
/// Defines common methods for INameValueList and IReadOnlyNameValueList.
/// </summary>
[PublicAPI]
public interface INameValueListBase<TValue>
{
    /// <summary>
    /// Returns the first Value of the given Name if one exists, otherwise null or default value.
    /// </summary>
    TValue? FirstOrDefault(string name);

    /// <summary>
    /// Gets the first Value of the given Name, if one exists.
    /// </summary>
    /// <returns>true if any item of the given name is found, otherwise false.</returns>
    bool TryGetFirst(string name, out TValue? value);

    /// <summary>
    /// Gets all Values of the given Name.
    /// </summary>
    IEnumerable<TValue> GetAll(string name);

    /// <summary>
    /// True if any items with the given Name exist.
    /// </summary>
    bool Contains(string name);

    /// <summary>
    /// True if any item with the given Name and Value exists.
    /// </summary>
    bool Contains(string name, TValue? value);
}

/// <summary>
/// Defines an ordered collection of Name/Value pairs where duplicate names are allowed but aren't typical.
/// </summary>
[PublicAPI]
public interface INameValueList<TValue> : IList<(string Name, TValue Value)>, INameValueListBase<TValue>
{
    /// <summary>
    /// Adds a new Name/Value pair.
    /// </summary>
    void Add(string name, TValue value);

    /// <summary>
    /// Replaces the first occurrence of the given Name with the given Value and removes any others,
    /// or adds a new Name/Value pair if none exist.
    /// </summary>
    void AddOrReplace(string name, TValue value);

    /// <summary>
    /// Removes all items of the given Name.
    /// </summary>
    /// <returns>true if any item of the given name is found, otherwise false.</returns>
    bool Remove(string name);
}

/// <summary>
/// Defines a read-only ordered collection of Name/Value pairs where duplicate names are allowed but aren't typical.
/// </summary>
[PublicAPI]
public interface IReadOnlyNameValueList<TValue>
    : IReadOnlyList<(string Name, TValue Value)>,
        INameValueListBase<TValue>;

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
    public void Add(string name, TValue value) => Add((name, value));

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
    public bool Remove(string name) => RemoveAll(x => x.Name.OrdinalEquals(name, !caseSensitiveNames)) > 0;

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
