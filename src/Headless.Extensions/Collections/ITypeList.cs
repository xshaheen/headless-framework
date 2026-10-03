// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;

namespace Headless.Collections;

/// <summary>A shortcut for <see cref="ITypeList{TBaseType}"/> to use object as base type.</summary>
public interface ITypeList : ITypeList<object>;

/// <summary>Extends <see cref="IList{Type}"/> to add restriction a specific base type.</summary>
/// <typeparam name="TBaseType">Base Type of <see cref="Type"/>s in this list</typeparam>
public interface ITypeList<in TBaseType> : IList<Type>
{
    /// <summary>Adds a type to list.</summary>
    /// <typeparam name="T">Type</typeparam>
    void Add<T>()
        where T : TBaseType;

    /// <summary>Adds a type to list if it's not already in the list.</summary>
    /// <typeparam name="T">Type</typeparam>
    /// <returns><see langword="true"/> if the type was added; <see langword="false"/> if it was already present.</returns>
    bool TryAdd<T>()
        where T : TBaseType;

    /// <summary>Checks if a type exists in the list.</summary>
    /// <typeparam name="T">Type</typeparam>
    /// <returns><see langword="true"/> if the type is in the list; otherwise, <see langword="false"/>.</returns>
    bool Contains<T>()
        where T : TBaseType;

    /// <summary>Removes a type from list</summary>
    /// <typeparam name="T">Type</typeparam>
    void Remove<T>()
        where T : TBaseType;
}
