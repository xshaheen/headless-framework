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

/// <summary>A shortcut for <see cref="TypeList{TBaseType}"/> to use object as base type.</summary>
public sealed class TypeList : TypeList<object>, ITypeList;

/// <summary>Extends <see cref="List{Type}"/> to add restriction a specific base type.</summary>
/// <typeparam name="TBaseType">Base Type of <see cref="Type"/>s in this list</typeparam>
public class TypeList<TBaseType> : ITypeList<TBaseType>
{
    private readonly List<Type> _typeList = [];

    public int Count => _typeList.Count;

    public bool IsReadOnly => false;

    /// <summary>Gets or sets the <see cref="Type"/> at the specified index.</summary>
    /// <param name="index">The zero-based index of the element to get or set.</param>
    /// <returns>The <see cref="Type"/> at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the list.</exception>
    /// <exception cref="ArgumentException">The assigned type is not assignable to <typeparamref name="TBaseType"/>.</exception>
    public Type this[int index]
    {
        get { return _typeList[index]; }
        set
        {
            _CheckType(value);
            _typeList[index] = value;
        }
    }

    public void Add<T>()
        where T : TBaseType
    {
        _typeList.Add(typeof(T));
    }

    public bool TryAdd<T>()
        where T : TBaseType
    {
        if (Contains<T>())
        {
            return false;
        }

        Add<T>();
        return true;
    }

    /// <summary>Adds a <see cref="Type"/> to the end of the list.</summary>
    /// <param name="item">The type to add.</param>
    /// <exception cref="ArgumentException"><paramref name="item"/> is not assignable to <typeparamref name="TBaseType"/>.</exception>
    public void Add(Type item)
    {
        _CheckType(item);
        _typeList.Add(item);
    }

    /// <summary>Inserts a <see cref="Type"/> into the list at the specified index.</summary>
    /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
    /// <param name="item">The type to insert.</param>
    /// <exception cref="ArgumentException"><paramref name="item"/> is not assignable to <typeparamref name="TBaseType"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the list.</exception>
    public void Insert(int index, Type item)
    {
        _CheckType(item);
        _typeList.Insert(index, item);
    }

    public int IndexOf(Type item)
    {
        return _typeList.IndexOf(item);
    }

    public bool Contains<T>()
        where T : TBaseType
    {
        return Contains(typeof(T));
    }

    public bool Contains(Type item)
    {
        return _typeList.Contains(item);
    }

    public void Remove<T>()
        where T : TBaseType
    {
        _typeList.Remove(typeof(T));
    }

    public bool Remove(Type item)
    {
        return _typeList.Remove(item);
    }

    public void RemoveAt(int index)
    {
        _typeList.RemoveAt(index);
    }

    public void Clear()
    {
        _typeList.Clear();
    }

    public void CopyTo(Type[] array, int arrayIndex)
    {
        _typeList.CopyTo(array, arrayIndex);
    }

    public IEnumerator<Type> GetEnumerator()
    {
        return _typeList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _typeList.GetEnumerator();
    }

    private static void _CheckType(Type item)
    {
        if (!typeof(TBaseType).GetTypeInfo().IsAssignableFrom(item))
        {
            throw new ArgumentException(
                $"Given type ({item.AssemblyQualifiedName}) should be instance of {typeof(TBaseType).AssemblyQualifiedName} ",
                nameof(item)
            );
        }
    }
}
