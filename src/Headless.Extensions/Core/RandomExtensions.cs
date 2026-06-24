// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

#pragma warning disable CA5394 // CA5394: Random is an insecure random number generator.
/// <summary>
/// Extension methods on <see cref="Random"/> for picking items from collections and generating values of additional
/// types. The underlying generator is not cryptographically secure; do not use for security-sensitive purposes.
/// </summary>
public static class RandomExtensions
{
    extension(Random random)
    {
        /// <summary>Returns a uniformly random element of <paramref name="array"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="array">The non-empty span to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="array"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="array"/> is empty.</exception>
        public T Pick<T>(params ReadOnlySpan<T> array)
        {
            Argument.IsNotNull(random);

            if (array.Length == 0)
            {
                throw new ArgumentException("Array is empty.", nameof(array));
            }

            var index = random.NextInt32(0, array.Length);

            return array[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="array"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="array">The non-empty array to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="array"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="array"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="array"/> is empty.</exception>
        public T GetItem<T>(T[] array)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(array);

            if (array.Length == 0)
            {
                throw new ArgumentException("Array is empty.", nameof(array));
            }

            var index = random.NextInt32(0, array.Length);

            return array[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty list to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        public T GetItem<T>(IList<T> list)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(list);

            if (list.Count == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Count);

            return list[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty collection to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        /// <remarks>
        /// This overload performs an O(n) traversal via <see cref="System.Linq.Enumerable.ElementAt{TSource}(System.Collections.Generic.IEnumerable{TSource}, int)"/>
        /// to reach the selected index because <see cref="ICollection{T}"/> does not expose index-based access.
        /// For large collections, prefer the <c>IList&lt;T&gt;</c>, array, or <c>ReadOnlySpan&lt;T&gt;</c> overloads,
        /// which index directly in O(1).
        /// </remarks>
        public T GetItem<T>(ICollection<T> list)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(list);

            if (list.Count == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Count);

            return list.ElementAt(index);
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty span to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        public T GetItem<T>(ReadOnlySpan<T> list)
        {
            Argument.IsNotNull(random);

            if (list.Length == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Length);

            return list[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty memory region to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        public T GetItem<T>(ReadOnlyMemory<T> list)
        {
            Argument.IsNotNull(random);

            if (list.Length == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Length);

            return list.Span[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty read-only list to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        public T GetItem<T>(IReadOnlyList<T> list)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(list);

            if (list.Count == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Count);

            return list[index];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty read-only collection to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        /// <remarks>
        /// This overload performs an O(n) traversal via <see cref="System.Linq.Enumerable.ElementAt{TSource}(System.Collections.Generic.IEnumerable{TSource}, int)"/>
        /// to reach the selected index because <see cref="IReadOnlyCollection{T}"/> does not expose index-based access.
        /// For large collections, prefer the <c>IReadOnlyList&lt;T&gt;</c>, array, or <c>ReadOnlySpan&lt;T&gt;</c> overloads,
        /// which index directly in O(1).
        /// </remarks>
        public T GetItem<T>(IReadOnlyCollection<T> list)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(list);

            if (list.Count == 0)
            {
                throw new ArgumentException("List is empty.", nameof(list));
            }

            var index = random.NextInt32(0, list.Count);

            return list.ElementAt(index);
        }

        /// <summary>Returns a uniformly random element of <paramref name="set"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="set">The non-empty set to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="set"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="set"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="set"/> is empty.</exception>
        public T GetItem<T>(HashSet<T> set)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(set);

            if (set.Count == 0)
            {
                throw new ArgumentException("List is empty.", nameof(set));
            }

            var index = random.NextInt32(0, set.Count);

            return set.ElementAt(index);
        }

        /// <summary>Returns a random <see cref="bool"/> with an even chance of <see langword="true"/> or <see langword="false"/>.</summary>
        /// <returns>A random boolean value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public bool NextBoolean()
        {
            Argument.IsNotNull(random);

            return random.Next(0, 2) != 0;
        }

        /// <summary>Returns a random <see cref="byte"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="byte"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public byte NextByte(byte min = 0, byte max = byte.MaxValue)
        {
            Argument.IsNotNull(random);

            return (byte)random.Next(min, max);
        }

        /// <summary>Returns a random <see cref="sbyte"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="sbyte"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public sbyte NextSByte(sbyte min = 0, sbyte max = sbyte.MaxValue)
        {
            Argument.IsNotNull(random);

            return (sbyte)random.Next(min, max);
        }

        /// <summary>Returns a random <see cref="DateTime"/> uniformly distributed in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound; should be greater than or equal to <paramref name="min"/>.</param>
        /// <returns>A random <see cref="DateTime"/> within the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public DateTime NextDateTime(DateTime min, DateTime max)
        {
            Argument.IsNotNull(random);

            var diff = max.Ticks - min.Ticks;
            var range = (long)(diff * random.NextDouble());

            return min + new TimeSpan(range);
        }

        /// <summary>Returns a random <see cref="double"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="double"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public double NextDouble(double min = 0D, double max = 1D)
        {
            Argument.IsNotNull(random);

            return (random.NextDouble() * (max - min)) + min;
        }

        /// <summary>Returns a random <see cref="short"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="short"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public short NextInt16(short min = 0, short max = short.MaxValue)
        {
            Argument.IsNotNull(random);

            return (short)random.Next(min, max);
        }

        /// <summary>Returns a random <see cref="int"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="int"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public int NextInt32(int min = 0, int max = int.MaxValue)
        {
            Argument.IsNotNull(random);

            return random.Next(min, max);
        }

        /// <summary>Returns a random <see cref="long"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="long"/> in the requested range, or <paramref name="min"/> when the bounds are equal.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public long NextInt64(long min = 0L, long max = long.MaxValue)
        {
            Argument.IsNotNull(random);

            return min == max ? min : (long)((random.NextDouble() * (max - min)) + min);
        }

        /// <summary>Returns a random <see cref="float"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="float"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public float NextSingle(float min = 0f, float max = 1f)
        {
            Argument.IsNotNull(random);

            return (float)random.NextDouble(min, max);
        }

        /// <summary>Returns a random <see cref="ushort"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="ushort"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public ushort NextUInt16(ushort min = 0, ushort max = ushort.MaxValue)
        {
            Argument.IsNotNull(random);

            return (ushort)random.Next(min, max);
        }

        /// <summary>Returns a random <see cref="uint"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="uint"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public uint NextUInt32(uint min = 0u, uint max = uint.MaxValue)
        {
            Argument.IsNotNull(random);

            return (uint)random.NextInt64(min, max);
        }

        /// <summary>Returns a random <see cref="ulong"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="ulong"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public ulong NextUInt64(ulong min = 0ul, ulong max = ulong.MaxValue)
        {
            Argument.IsNotNull(random);

            Span<byte> buffer = stackalloc byte[sizeof(ulong)];
            random.NextBytes(buffer);

            return (MemoryMarshal.Read<ulong>(buffer) * (max - min) / ulong.MaxValue) + min;
        }

        /// <summary>Returns a random <see cref="decimal"/> in the range <c>[<paramref name="min"/>, <paramref name="max"/>)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A random <see cref="decimal"/> in the requested range.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is <see langword="null"/>.</exception>
        public decimal NextDecimal(decimal min = decimal.MinValue, decimal max = decimal.MaxValue)
        {
            Argument.IsNotNull(random);

            // Linear interpolation (min*(1-t) + max*t) instead of min + t*(max-min): the latter overflows
            // when the range spans most of the decimal domain (e.g. the default MinValue..MaxValue). Each term
            // here is bounded by |min| or |max|, so the convex combination never leaves the decimal range.
            var t = (decimal)random.NextDouble();

            return (min * (1m - t)) + (max * t);
        }

        /// <summary>Builds a random string of exactly <paramref name="length"/> characters drawn from <paramref name="chars"/>.</summary>
        /// <param name="length">The exact length of the string to generate.</param>
        /// <param name="chars">The pool of characters to draw from.</param>
        /// <returns>A random string composed of characters from <paramref name="chars"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="chars"/> is <see langword="null"/>.</exception>
        public string NextString(int length, string chars)
        {
            return random.NextString(length, length, chars);
        }

        /// <summary>Builds a random string whose length is between <paramref name="minLength"/> and <paramref name="maxLength"/> (both inclusive), drawn from <paramref name="chars"/>.</summary>
        /// <param name="minLength">The minimum length of the string to generate.</param>
        /// <param name="maxLength">The maximum length of the string to generate; must be greater than or equal to <paramref name="minLength"/>.</param>
        /// <param name="chars">The pool of characters to draw from.</param>
        /// <returns>A random string composed of characters from <paramref name="chars"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> or <paramref name="chars"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxLength"/> is less than <paramref name="minLength"/>.</exception>
        public string NextString(int minLength, int maxLength, string chars)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(chars);

            var length = minLength + random.Next(0, maxLength - minLength + 1); // length of the string

            // Fill the result span directly (one allocation, no StringBuilder buffer churn).
            return string.Create(
                length,
                (random, chars),
                static (span, state) =>
                {
                    var (rnd, pool) = state;
                    var max = pool.Length; // number of available characters

                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = pool[rnd.Next(0, max)];
                    }
                }
            );
        }

        /// <summary>Returns a uniformly random element of <paramref name="objects"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="objects">The non-empty span to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="objects"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="objects"/> is empty.</exception>
        public T GetRandomOf<T>(params ReadOnlySpan<T> objects)
        {
            Argument.IsNotEmpty(objects);

            return objects[random.Next(0, objects.Length)];
        }

        /// <summary>Returns a uniformly random element of <paramref name="list"/>.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The non-empty list to pick from.</param>
        /// <returns>A randomly selected element of <paramref name="list"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="list"/> is empty.</exception>
        public T GetRandomOfList<T>(IList<T> list)
        {
            Argument.IsNotNullOrEmpty(list);

            return list[random.Next(0, list.Count)];
        }

        /// <summary>Returns a new list containing every element of <paramref name="items"/> in a uniformly shuffled order.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The non-empty sequence to shuffle.</param>
        /// <returns>A new <see cref="List{T}"/> containing the elements of <paramref name="items"/> in random order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="items"/> is empty.</exception>
        public List<T> GenerateRandomizedList<T>(IEnumerable<T> items)
        {
            Argument.IsNotNullOrEmpty(items);

            var result = items.ToList();
            random.Shuffle(result.AsSpan());

            return result;
        }
    }
}
