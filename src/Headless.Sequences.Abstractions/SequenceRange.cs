// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Sequences;

/// <summary>
/// Represents a contiguous block of values allocated by <see cref="ISequenceGenerator.ReserveAsync" />.
/// </summary>
/// <remarks>
/// A sequence range is evenly spaced and represented by <see cref="First"/>, <see cref="Step"/>, and <see cref="Count"/>.
/// Enumerating the range uses a struct enumerator that allocates no memory.
/// </remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SequenceRange : IEnumerable<long>
{
    /// <summary>Initializes a sequence range.</summary>
    /// <param name="first">The first value in the range.</param>
    /// <param name="count">The number of values in the range. Must be at least 1.</param>
    /// <param name="step">The difference between adjacent values. Must be greater than 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> or <paramref name="step" /> is not positive.</exception>
    /// <exception cref="OverflowException">The end of the range exceeds <see cref="long.MaxValue" />.</exception>
    public SequenceRange(long first, int count, long step)
    {
        Argument.IsPositive(count);
        Argument.IsPositive(step);

        // Validates overflow once during construction so Last and enumerators can use unchecked arithmetic.
        _ = checked(first + ((count - 1) * step));

        First = first;
        Count = count;
        Step = step;
    }

    /// <summary>Gets the first value in the range.</summary>
    public long First { get; }

    /// <summary>Gets the number of values in the range.</summary>
    public int Count { get; }

    /// <summary>Gets the difference between adjacent values.</summary>
    public long Step { get; }

    /// <summary>Gets the last value in the range, representing the counter value after allocation.</summary>
    public long Last => First + ((Count - 1) * Step);

    /// <summary>Returns an enumerator that iterates through the sequence values in ascending order.</summary>
    /// <returns>A non-allocating struct enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<long> IEnumerable<long>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates values in a <see cref="SequenceRange" /> without allocating heap memory.</summary>
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
