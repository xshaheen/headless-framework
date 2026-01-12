// Copyright (c) Mahmoud Shaheen. All rights reserved.

using CommunityToolkit.HighPerformance;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

#pragma warning disable CA5394 // CA5394: Random is an insecure random number generator.
public static class RandomExtensions
{
    extension(Random random)
    {
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

        public bool NextBoolean()
        {
            Argument.IsNotNull(random);

            return random.Next(0, 2) != 0;
        }

        public byte NextByte(byte min = 0, byte max = byte.MaxValue)
        {
            Argument.IsNotNull(random);

            return (byte) random.Next(min, max);
        }

        public sbyte NextSByte(sbyte min = 0, sbyte max = sbyte.MaxValue)
        {
            Argument.IsNotNull(random);

            return (sbyte) random.Next(min, max);
        }

        public DateTime NextDateTime(DateTime min, DateTime max)
        {
            Argument.IsNotNull(random);

            var diff = max.Ticks - min.Ticks;
            var range = (long) (diff * random.NextDouble());

            return min + new TimeSpan(range);
        }

        public double NextDouble(double min = 0D, double max = 1D)
        {
            Argument.IsNotNull(random);

            return (random.NextDouble() * (max - min)) + min;
        }

        public short NextInt16(short min = 0, short max = short.MaxValue)
        {
            Argument.IsNotNull(random);

            return (short) random.Next(min, max);
        }

        public int NextInt32(int min = 0, int max = int.MaxValue)
        {
            Argument.IsNotNull(random);

            return random.Next(min, max);
        }

        public long NextInt64(long min = 0L, long max = long.MaxValue)
        {
            Argument.IsNotNull(random);

            return min == max ? min : (long) ((random.NextDouble() * (max - min)) + min);
        }

        public float NextSingle(float min = 0f, float max = 1f)
        {
            Argument.IsNotNull(random);

            return (float) random.NextDouble(min, max);
        }

        public ushort NextUInt16(ushort min = 0, ushort max = ushort.MaxValue)
        {
            Argument.IsNotNull(random);

            return (ushort) random.Next(min, max);
        }

        public uint NextUInt32(uint min = 0u, uint max = uint.MaxValue)
        {
            Argument.IsNotNull(random);

            return (uint) random.NextInt64(min, max);
        }

        public ulong NextUInt64(ulong min = 0ul, ulong max = ulong.MaxValue)
        {
            Argument.IsNotNull(random);

            var buffer = new byte[sizeof(long)];
            random.NextBytes(buffer);

            return (BitConverter.ToUInt64(buffer, 0) * (max - min) / ulong.MaxValue) + min;
        }

        public decimal NextDecimal(
            decimal min = decimal.MinValue,
            decimal max = decimal.MaxValue
        )
        {
            Argument.IsNotNull(random);

            return ((decimal) random.NextDouble() * (max - min)) + min;
        }

        public string NextString(int length, string chars)
        {
            return random.NextString(length, length, chars);
        }

        public string NextString(int minLength, int maxLength, string chars)
        {
            Argument.IsNotNull(random);
            Argument.IsNotNull(chars);

            var length = minLength + random.Next(0, maxLength - minLength + 1); // length of the string

            var max = chars.Length; // number of available characters
            var sb = new StringBuilder(length);

            for (var i = 0; i < length; i++)
            {
                sb.Append(chars[random.Next(0, max)]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets random of given objects.
        /// </summary>
        /// <typeparam name="T">Type of the objects</typeparam>
        /// <param name="objects">List of object to select a random one</param>
        public T GetRandomOf<T>(params ReadOnlySpan<T> objects)
        {
            Argument.IsNotEmpty(objects);

            return objects[random.Next(0, objects.Length)];
        }

        /// <summary>
        /// Gets random item from the given list.
        /// </summary>
        /// <typeparam name="T">Type of the objects</typeparam>
        /// <param name="list">List of object to select a random one</param>
        public T GetRandomOfList<T>(IList<T> list)
        {
            Argument.IsNotNullOrEmpty(list);

            return list[random.Next(0, list.Count)];
        }

        /// <summary>
        /// Generates a randomized list from given enumerable.
        /// </summary>
        /// <typeparam name="T">Type of items in the list</typeparam>
        /// <param name="items">items</param>
        public List<T> GenerateRandomizedList<T>(IEnumerable<T> items)
        {
            Argument.IsNotNullOrEmpty(items);

            var result = items.ToList();
            Random.Shared.Shuffle(result.AsSpan());

            return result;
        }
    }
}
