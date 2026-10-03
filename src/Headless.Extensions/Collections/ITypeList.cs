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

    /// <summary>Gets the number of types contained in the list.</summary>
    public int Count => _typeList.Count;

    /// <summary>Gets a value indicating whether the list is read-only. Always <see langword="false"/>.</summary>
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

    /// <inheritdoc/>
    public void Add<T>()
        where T : TBaseType
    {
        _typeList.Add(typeof(T));
    }

    /// <inheritdoc/>
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

    /// <summary>Returns the zero-based index of the first occurrence of <paramref name="item"/> in the list, or <c>-1</c> if not found.</summary>
    /// <param name="item">The type to locate.</param>
    /// <returns>The zero-based index of <paramref name="item"/>, or <c>-1</c> if it is not found.</returns>
    public int IndexOf(Type item)
    {
        return _typeList.IndexOf(item);
    }

    /// <inheritdoc/>
    public bool Contains<T>()
        where T : TBaseType
    {
        return Contains(typeof(T));
    }

    /// <summary>Determines whether the list contains the specified type.</summary>
    /// <param name="item">The type to locate.</param>
    /// <returns><see langword="true"/> if <paramref name="item"/> is found; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Type item)
    {
        return _typeList.Contains(item);
    }

    /// <inheritdoc/>
    public void Remove<T>()
        where T : TBaseType
    {
        _typeList.Remove(typeof(T));
    }

    /// <summary>Removes the first occurrence of the specified type from the list.</summary>
    /// <param name="item">The type to remove.</param>
    /// <returns><see langword="true"/> if <paramref name="item"/> was removed; <see langword="false"/> if it was not found.</returns>
    public bool Remove(Type item)
    {
        return _typeList.Remove(item);
    }

    /// <summary>Removes the element at the specified index.</summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the list.</exception>
    public void RemoveAt(int index)
    {
        _typeList.RemoveAt(index);
    }

    /// <summary>Removes all types from the list.</summary>
    public void Clear()
    {
        _typeList.Clear();
    }

    /// <summary>Copies the types in the list to <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than zero.</exception>
    /// <exception cref="ArgumentException">The number of elements exceeds the available space from <paramref name="arrayIndex"/> to the end of <paramref name="array"/>.</exception>
    public void CopyTo(Type[] array, int arrayIndex)
    {
        _typeList.CopyTo(array, arrayIndex);
    }

    /// <summary>Returns an enumerator that iterates through the types in the list.</summary>
    /// <returns>An <see cref="IEnumerator{T}"/> for the list.</returns>
    public IEnumerator<Type> GetEnumerator()
    {
        return _typeList.GetEnumerator();
    }

    /// <inheritdoc/>
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
