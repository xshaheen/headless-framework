// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Core;

namespace Tests.Core;

public sealed class SequentialGuidTests
{
    [Fact]
    public void next_sequential_at_end_should_return_unique_guids()
    {
        // given
        const int count = 1000;
        var guids = new HashSet<Guid>();

        // when
        for (var i = 0; i < count; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAtEnd());
        }

        // then
        guids.Should().HaveCount(count);
    }

    [Fact]
    public void next_sequential_at_end_should_be_sequential()
    {
        // given - generate multiple GUIDs and verify ordering
        var guids = new List<Guid>();
        for (var i = 0; i < 100; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAtEnd());
        }

        // when - compare adjacent pairs using SQL Server ordering (byte array comparison at end)
        var allOrdered = true;
        for (var i = 0; i < guids.Count - 1; i++)
        {
            var firstBytes = guids[i].ToByteArray();
            var secondBytes = guids[i + 1].ToByteArray();

            // SQL Server orders by bytes 10-15, 8-9, 6-7, 4-5, 0-3 (Data4, then Data3, etc.)
            // The sequential counter is placed in bytes 8-15
            var firstKey = new byte[]
            {
                firstBytes[10],
                firstBytes[11],
                firstBytes[12],
                firstBytes[13],
                firstBytes[14],
                firstBytes[15],
                firstBytes[8],
                firstBytes[9],
            };
            var secondKey = new byte[]
            {
                secondBytes[10],
                secondBytes[11],
                secondBytes[12],
                secondBytes[13],
                secondBytes[14],
                secondBytes[15],
                secondBytes[8],
                secondBytes[9],
            };

            if (_CompareByteArrays(firstKey, secondKey) >= 0)
            {
                allOrdered = false;
                break;
            }
        }

        // then
        allOrdered.Should().BeTrue("sequential GUIDs should sort in order when using SQL Server byte ordering");
    }

    [Fact]
    public void next_sequential_as_string_should_return_unique_guids()
    {
        // given
        const int count = 1000;
        var guids = new HashSet<Guid>();

        // when
        for (var i = 0; i < count; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAsString());
        }

        // then
        guids.Should().HaveCount(count);
    }

    [Fact]
    public void next_sequential_as_string_should_be_mostly_sequential_over_time()
    {
        // given - collect GUIDs over a span of time to ensure timestamp differences
        var guids = new List<string>();

        // when - generate GUIDs with small delays to ensure timestamp progression
        for (var i = 0; i < 10; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAsString().ToString());
            if (i < 9)
            {
                Thread.Sleep(2); // Small delay to ensure timestamp advances
            }
        }

        // then - verify they are in ascending order when sorted
        // The timestamp is in the first 6 bytes, so string comparison should work
        var sorted = guids.OrderBy(g => g, StringComparer.Ordinal).ToList();
        guids.Should().Equal(sorted, "GUIDs generated over time should be sequential in string format");
    }

    [Fact]
    public void next_sequential_as_binary_should_return_unique_guids()
    {
        // given
        const int count = 1000;
        var guids = new HashSet<Guid>();

        // when
        for (var i = 0; i < count; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAsBinary());
        }

        // then
        guids.Should().HaveCount(count);
    }

    [Fact]
    public void next_sequential_as_binary_should_have_monotonic_timestamps()
    {
        // given - binary sequential places timestamp in bytes 0-5
        var guids = new List<Guid>();
        for (var i = 0; i < 100; i++)
        {
            guids.Add(SequentialGuid.NextSequentialAsBinary());
        }

        // when - extract and compare timestamp portions
        var allMonotonic = true;
        for (var i = 0; i < guids.Count - 1; i++)
        {
            var firstBytes = guids[i].ToByteArray();
            var secondBytes = guids[i + 1].ToByteArray();

            // Timestamp is in bytes 0-5 (6 bytes, big-endian)
            var firstTimestamp = new byte[]
            {
                firstBytes[0],
                firstBytes[1],
                firstBytes[2],
                firstBytes[3],
                firstBytes[4],
                firstBytes[5],
            };
            var secondTimestamp = new byte[]
            {
                secondBytes[0],
                secondBytes[1],
                secondBytes[2],
                secondBytes[3],
                secondBytes[4],
                secondBytes[5],
            };

            var comparison = _CompareByteArrays(firstTimestamp, secondTimestamp);
            if (comparison > 0) // timestamp should never decrease
            {
                allMonotonic = false;
                break;
            }
        }

        // then
        allMonotonic.Should().BeTrue("timestamps in sequential binary GUIDs should be monotonically non-decreasing");
    }

    [Fact]
    public void should_be_thread_safe()
    {
        // given
        const int threadCount = 10;
        const int guidsPerThread = 100;
        var allGuids = new ConcurrentBag<Guid>();

        // when
        Parallel.For(
            0,
            threadCount,
            _ =>
            {
                for (var i = 0; i < guidsPerThread; i++)
                {
                    allGuids.Add(SequentialGuid.NextSequentialAtEnd());
                    allGuids.Add(SequentialGuid.NextSequentialAsString());
                    allGuids.Add(SequentialGuid.NextSequentialAsBinary());
                }
            }
        );

        // then
        var uniqueGuids = new HashSet<Guid>(allGuids);
        uniqueGuids.Should().HaveCount(threadCount * guidsPerThread * 3);
    }

    private static int _CompareByteArrays(byte[] first, byte[] second)
    {
        for (var i = 0; i < Math.Min(first.Length, second.Length); i++)
        {
            var comparison = first[i].CompareTo(second[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return first.Length.CompareTo(second.Length);
    }
}
