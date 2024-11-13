// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

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
    bool TryAdd<T>()
        where T : TBaseType;

    /// <summary>Checks if a type exists in the list.</summary>
    /// <typeparam name="T">Type</typeparam>
    /// <returns></returns>
    bool Contains<T>()
        where T : TBaseType;

    /// <summary>Removes a type from list</summary>
    /// <typeparam name="T"></typeparam>
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

    public void Add(Type item)
    {
        _CheckType(item);
        _typeList.Add(item);
    }

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
