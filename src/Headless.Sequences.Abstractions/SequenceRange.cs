// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Sequences;

/// <summary>
/// A block of consecutive values taken by one <see cref="ISequenceGenerator.ReserveAsync" /> call:
/// <see cref="First" />, then each following value <see cref="Step" /> apart, <see cref="Count" /> values in all.
/// </summary>
/// <remarks>
/// A value rather than a list because a reserved block is always evenly spaced, so three numbers describe it
/// completely. <c>foreach</c> over it uses a struct enumerator and allocates nothing.
/// </remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SequenceRange : IEnumerable<long>
{
    /// <summary>Creates a range.</summary>
    /// <param name="first">The first value in the range.</param>
    /// <param name="count">How many values the range holds; at least 1.</param>
    /// <param name="step">The distance between two neighbouring values; greater than 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> or <paramref name="step" /> is not positive.</exception>
    /// <exception cref="OverflowException">The last value would run past <see cref="long.MaxValue" />.</exception>
    public SequenceRange(long first, int count, long step)
    {
        Argument.IsPositive(count);
        Argument.IsPositive(step);

        // Checked once here so Last and the enumerator can use plain arithmetic.
        _ = checked(first + ((count - 1) * step));

        First = first;
        Count = count;
        Step = step;
    }

    /// <summary>Gets the first value in the range.</summary>
    public long First { get; }

    /// <summary>Gets how many values the range holds.</summary>
    public int Count { get; }

    /// <summary>Gets the distance between two neighbouring values.</summary>
    public long Step { get; }

    /// <summary>Gets the last value in the range, which is also the counter's value after the reservation.</summary>
    public long Last => First + ((Count - 1) * Step);

    /// <summary>Returns an enumerator over the range's values, in ascending order.</summary>
    /// <returns>A struct enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<long> IEnumerable<long>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the values of a <see cref="SequenceRange" /> without allocating.</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct Enumerator : IEnumerator<long>
    {
        private readonly SequenceRange _range;
        private int _index;

        internal Enumerator(SequenceRange range)
        {
            _range = range;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly long Current => _range.First + (_index * _range.Step);

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_index + 1 >= _range.Count)
            {
                return false;
            }

            _index++;

            return true;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose() { }
    }
}
