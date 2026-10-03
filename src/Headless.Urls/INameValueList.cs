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
