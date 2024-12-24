// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Core;

#pragma warning disable CA5394, CA1002
/// <summary>A shortcut to use <see cref="Random"/> class. Also provides some useful methods.</summary>
[PublicAPI]
public static class RandomHelper
{
    private static readonly Random _Random = new();

    /// <summary>
    /// Returns a random number within a specified range.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
    /// <param name="maxValue">
    /// The exclusive upper bound of the random number returned. maxValue must be greater than or equal
    /// to minValue.
    /// </param>
    /// <returns>
    /// A 32-bit signed integer greater than or equal to minValue and less than maxValue;
    /// that is, the range of return values includes minValue but not maxValue.
    /// If minValue equals maxValue, minValue is returned.
    /// </returns>
    public static int GetRandom(int minValue, int maxValue)
    {
        lock (_Random)
        {
            return _Random.Next(minValue, maxValue);
        }
    }

    /// <summary>
    /// Returns a nonnegative random number less than the specified maximum.
    /// </summary>
    /// <param name="maxValue">
    /// The exclusive upper bound of the random number to be generated. maxValue must be greater than or
    /// equal to zero.
    /// </param>
    /// <returns>
    /// A 32-bit signed integer greater than or equal to zero, and less than maxValue;
    /// that is, the range of return values ordinarily includes zero but not maxValue.
    /// However, if maxValue equals zero, maxValue is returned.
    /// </returns>
    public static int GetRandom(int maxValue)
    {
        lock (_Random)
        {
            return _Random.Next(maxValue);
        }
    }

    /// <summary>
    /// Returns a nonnegative random number.
    /// </summary>
    /// <returns>
    /// A 32-bit signed integer greater than or equal to zero and less than <see cref="int.MaxValue"/>.
    /// </returns>
    public static int GetRandom()
    {
        lock (_Random)
        {
            return _Random.Next();
        }
    }

    /// <summary>
    /// Gets random of given objects.
    /// </summary>
    /// <typeparam name="T">Type of the objects</typeparam>
    /// <param name="objects">List of object to select a random one</param>
    public static T GetRandomOf<T>(params T[] objects)
    {
        Argument.IsNotNullOrEmpty(objects);

        return objects[GetRandom(0, objects.Length)];
    }

    /// <summary>
    /// Gets random item from the given list.
    /// </summary>
    /// <typeparam name="T">Type of the objects</typeparam>
    /// <param name="list">List of object to select a random one</param>
    public static T GetRandomOfList<T>(IList<T> list)
    {
        Argument.IsNotNullOrEmpty(list);

        return list[GetRandom(0, list.Count)];
    }

    /// <summary>
    /// Generates a randomized list from given enumerable.
    /// </summary>
    /// <typeparam name="T">Type of items in the list</typeparam>
    /// <param name="items">items</param>
    public static List<T> GenerateRandomizedList<T>(IList<T> items)
    {
        Argument.IsNotNullOrEmpty(items);

        List<T> randomList = [];

        while (items.Count > 0)
        {
            var randomIndex = GetRandom(0, items.Count);
            randomList.Add(items[randomIndex]);
            items.RemoveAt(randomIndex);
        }

        return randomList;
    }
}
#pragma warning restore CA5394, CA1002
