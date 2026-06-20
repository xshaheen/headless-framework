// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using Headless.Checks;

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static partial class EnumerableExtensions
{
    /// <summary>Returns the elements of the specified sequence or an empty sequence if the sequence is <see langword="null"/>.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to process.</param>
    /// <returns><paramref name="source"/> if it is not <see langword="null"/>; otherwise, <see cref="Enumerable.Empty{TSource}"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? source)
    {
        return source ?? [];
    }

    /// <summary>
    ///   Converts an <see cref="IEnumerable{T}" /> to an <see cref="ICollection{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="ICollection{T}"/>; or a new ICollection&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static ICollection<T> AsICollection<T>(this IEnumerable<T> source)
    {
        return source as ICollection<T> ?? source.ToList();
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to a <see cref="List{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="List{T}"/>; or a new List&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static List<T> AsIList<T>(this IEnumerable<T> source)
    {
        return source as List<T> ?? source.ToList();
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to a <see cref="List{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="List{T}"/>; or a new List&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static List<T> AsList<T>(this IEnumerable<T> source)
    {
        return source as List<T> ?? source.ToList();
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{TElement}" /> to an <see cref="Array" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="Array"/>; or a new T[] created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static T[] AsArray<T>(this IEnumerable<T> source)
    {
        return source as T[] ?? source.ToArray();
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to an <see cref="ISet{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <param name="comparer">Equality comparer to create the new Set if needed.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="ISet{T}"/>; or a new ISet&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static ISet<T> AsISet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
    {
        return source as ISet<T> ?? source.ToHashSet(comparer);
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to an <see cref="HashSet{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <param name="comparer">Equality comparer to create the new Set if needed.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="HashSet{T}"/>; or a new HashSet&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static HashSet<T> AsHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
    {
        return source as HashSet<T> ?? source.ToHashSet(comparer);
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to an <see cref="IReadOnlyCollection{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="IReadOnlyCollection{T}"/>; or a new IReadOnlyCollection&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static IReadOnlyCollection<T> AsIReadOnlyCollection<T>(this IEnumerable<T> source)
    {
        return source as IReadOnlyCollection<T> ?? new ReadOnlyCollection<T>(source.AsList());
    }

    /// <summary>
    /// Converts an <see cref="IEnumerable{T}" /> to an <see cref="IReadOnlyList{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">A sequence to convert.</param>
    /// <returns>
    /// Either <paramref name="source"/> if it can be cast to <see cref="IReadOnlyList{T}"/>; or a new IReadOnlyList&lt;T&gt; created from <c>source</c>.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static IReadOnlyList<T> AsIReadOnlyList<T>(this IEnumerable<T> source)
    {
        return source as IReadOnlyList<T> ?? new ReadOnlyCollection<T>(source.AsList());
    }

    /// <summary>
    /// Returns the source as a concrete <see cref="Dictionary{TKey,TValue}"/>: the same instance if it already is one;
    /// otherwise, a new dictionary copied from its pairs.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <param name="dict">The dictionary to convert.</param>
    /// <returns>Either <paramref name="dict"/> if it is a <see cref="Dictionary{TKey,TValue}"/>; or a new dictionary copied from it.</returns>
    public static Dictionary<TKey, TValue> AsDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict)
        where TKey : notnull
    {
        if (dict is Dictionary<TKey, TValue> dictionary)
        {
            return dictionary;
        }

        return dict.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Concatenates the members of a constructed <see cref="IEnumerable{T}"/> collection of type
    /// System.String, using the
    /// specified separator between each member.
    /// This is a shortcut for string.Join(...)
    /// </summary>
    /// <param name="source">A collection that contains the strings to concatenate.</param>
    /// <param name="separator">
    /// The string to use as a separator. separator is included in the returned string only if values
    /// has more than one element.
    /// </param>
    /// <returns>
    /// A string that consists of the members of values delimited by the separator string.
    /// If values have no members, the method returns System.String.Empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string JoinAsString(this IEnumerable<string> source, string separator)
    {
        return string.Join(separator, source);
    }

    /// <summary>
    /// Concatenates the members of a constructed <see cref="IEnumerable{T}"/> collection of type
    /// System.String, using the
    /// specified separator between each member.
    /// This is a shortcut for string.Join(...)
    /// </summary>
    /// <param name="source">A collection that contains the strings to concatenate.</param>
    /// <param name="separator">
    /// The string to use as a separator. separator is included in the returned string only if values
    /// has more than one element.
    /// </param>
    /// <returns>
    /// A string that consists of the members of values delimited by the separator string.
    /// If values have no members, the method returns System.String.Empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string JoinAsString(this IEnumerable<string> source, char separator)
    {
        return string.Join(separator, source);
    }

    /// <summary>
    /// Concatenates the members of a collection, using the specified separator between each member.
    /// This is a shortcut for string.Join(...)
    /// </summary>
    /// <param name="source">A collection that contains the objects to concatenate.</param>
    /// <param name="separator">
    /// The string to use as a separator. separator is included in the returned string only if values
    /// has more than one element.
    /// </param>
    /// <typeparam name="T">The type of the members of values.</typeparam>
    /// <returns>
    /// A string that consists of the members of values delimited by the separator string.
    /// If values have no members, the method returns System.String.Empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string JoinAsString<T>(this IEnumerable<T> source, string separator)
    {
        return string.Join(separator, source);
    }

    /// <summary>
    /// Concatenates the members of a collection, using the specified separator between each member.
    /// This is a shortcut for string.Join(...)
    /// </summary>
    /// <param name="source">A collection that contains the objects to concatenate.</param>
    /// <param name="separator">
    /// The string to use as a separator. separator is included in the returned string only if values
    /// has more than one element.
    /// </param>
    /// <typeparam name="T">The type of the members of values.</typeparam>
    /// <returns>
    /// A string that consists of the members of values delimited by the separator string.
    /// If values have no members, the method returns System.String.Empty.
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static string JoinAsString<T>(this IEnumerable<T> source, char separator)
    {
        return string.Join(separator, source);
    }

    /// <summary>
    /// Filters a <see cref="IEnumerable{T}"/> by given predicate if given condition is true.
    /// </summary>
    /// <param name="source">Enumerable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the enumerable</param>
    /// <returns>Filtered or not filtered enumerable based on <paramref name="condition"/></returns>
    [SystemPure]
    [JetBrainsPure]
    public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> source, bool condition, Func<T, bool> predicate)
    {
        return condition ? source.Where(predicate) : source;
    }

    /// <summary>
    /// Filters a <see cref="IEnumerable{T}"/> by given predicate if given condition is true.
    /// </summary>
    /// <param name="source">Enumerable to apply filtering</param>
    /// <param name="condition">A boolean value</param>
    /// <param name="predicate">Predicate to filter the enumerable</param>
    /// <returns>Filtered or not filtered enumerable based on <paramref name="condition"/></returns>
    [SystemPure]
    [JetBrainsPure]
    public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> source, bool condition, Func<T, int, bool> predicate)
    {
        return condition ? source.Where(predicate) : source;
    }

    /// <summary>
    /// Determines whether the sequence contains two or more elements that share the same projected key.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="list"/>.</typeparam>
    /// <typeparam name="TProp">The type of the key projected by <paramref name="selector"/>.</typeparam>
    /// <param name="list">The sequence to inspect.</param>
    /// <param name="selector">A function that projects each element to the key used for duplicate detection.</param>
    /// <returns><see langword="true"/> if any projected key occurs more than once; otherwise, <see langword="false"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static bool HasDuplicates<T, TProp>(this IEnumerable<T> list, Func<T, TProp> selector)
    {
        var d = new HashSet<TProp>();

        return list.Any(t => !d.Add(selector(t)));
    }

    /// <summary>Awaits a task that produces a sequence and materializes the result into a <see cref="List{T}"/>.</summary>
    /// <typeparam name="T">The type of the elements of the sequence.</typeparam>
    /// <param name="task">The task that produces the sequence.</param>
    /// <returns>A <see cref="List{T}"/> containing the elements produced by <paramref name="task"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is <see langword="null"/>.</exception>
    public static async Task<List<T>> ToListAsync<T>(this Task<IEnumerable<T>> task)
    {
#pragma warning disable VSTHRD003
        var result = await Argument.IsNotNull(task).ConfigureAwait(false);
#pragma warning restore VSTHRD003

        return result.ToList();
    }

    /// <summary>Awaits a task that produces a sequence and materializes the result into an array.</summary>
    /// <typeparam name="T">The type of the elements of the sequence.</typeparam>
    /// <param name="task">The task that produces the sequence.</param>
    /// <returns>An array containing the elements produced by <paramref name="task"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is <see langword="null"/>.</exception>
    public static async Task<T[]> ToArrayAsync<T>(this Task<IEnumerable<T>> task)
    {
#pragma warning disable VSTHRD003
        var result = await Argument.IsNotNull(task).ConfigureAwait(false);
#pragma warning restore VSTHRD003

        return result.ToArray();
    }

    /// <summary>
    /// Wraps the sequence so it can be enumerated only once. Enumerating the returned sequence a second time throws
    /// an <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The sequence to guard against multiple enumeration.</param>
    /// <returns>A sequence that permits only a single enumeration of <paramref name="enumerable"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown during a second enumeration of the returned sequence.</exception>
    public static IEnumerable<T> AsEnumerableOnce<T>(this IEnumerable<T> enumerable)
    {
        return new EnumerableOnce<T>(enumerable);
    }

    private sealed class EnumerableOnce<TSource>(IEnumerable<TSource> source) : IEnumerable<TSource>
    {
        private bool _enumerated;

        [MustDisposeResource]
        public IEnumerator<TSource> GetEnumerator()
        {
            if (_enumerated)
            {
                throw new InvalidOperationException("The source is already enumerated");
            }

            _enumerated = true;

            return source.GetEnumerator();
        }

        [MustDisposeResource]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
