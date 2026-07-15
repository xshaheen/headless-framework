// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Core;

namespace Tests.Core;

public sealed class SequentialGuidTests
{
    [Fact]
    public void should_return_unique_guids_when_next_sequential_at_end()
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
    public void should_be_sequential_when_next_sequential_at_end()
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
            var firstKey = new[]
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
            var secondKey = new[]
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
                }
            }
        );

        // then
        var uniqueGuids = new HashSet<Guid>(allGuids);
        uniqueGuids.Should().HaveCount(threadCount * guidsPerThread);
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
