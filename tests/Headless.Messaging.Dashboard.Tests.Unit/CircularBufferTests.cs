// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Testing.Tests;

namespace Tests;

public sealed class CircularBufferTests : TestBase
{
    // CircularBuffer is internal, so we need to use reflection to create and test it
    private static readonly Type _CircularBufferType = typeof(Headless.Messaging.Dashboard.DashboardOptions)
        .Assembly
        .GetType("Headless.Messaging.Dashboard.CircularBuffer`1")!;

    private static object _CreateBuffer(int capacity)
    {
        var genericType = _CircularBufferType.MakeGenericType(typeof(int));
        return Activator.CreateInstance(genericType, capacity)!;
    }

    private static void _Add(object buffer, int item)
    {
        buffer.GetType().GetMethod("Add")!.Invoke(buffer, [item]);
    }

    private static int _GetCount(object buffer)
    {
        return (int)buffer.GetType().GetProperty("Count")!.GetValue(buffer)!;
    }

    private static int _GetCapacity(object buffer)
    {
        return (int)buffer.GetType().GetProperty("Capacity")!.GetValue(buffer)!;
    }

    private static bool _IsFull(object buffer)
    {
        return (bool)buffer.GetType().GetProperty("IsFull")!.GetValue(buffer)!;
    }

    private static int _GetItem(object buffer, int index)
    {
        return (int)buffer.GetType().GetProperty("Item")!.GetValue(buffer, [index])!;
    }

    private static int[] _ToArray(object buffer)
    {
        return (int[])buffer.GetType().GetMethod("ToArray")!.Invoke(buffer, null)!;
    }

    private static void _Clear(object buffer)
    {
        buffer.GetType().GetMethod("Clear")!.Invoke(buffer, null);
    }

    [Fact]
    public void should_maintain_fixed_capacity()
    {
        // given
        const int capacity = 5;
        var buffer = _CreateBuffer(capacity);

        // when
        for (var i = 0; i < 10; i++)
        {
            _Add(buffer, i);
        }

        // then
        _GetCapacity(buffer).Should().Be(capacity);
        _GetCount(buffer).Should().Be(capacity);
        _IsFull(buffer).Should().BeTrue();
    }

    [Fact]
    public void should_overwrite_oldest_on_overflow()
    {
        // given
        const int capacity = 3;
        var buffer = _CreateBuffer(capacity);

        // when - add more items than capacity
        _Add(buffer, 1);
        _Add(buffer, 2);
        _Add(buffer, 3);
        _Add(buffer, 4); // overwrites 1
        _Add(buffer, 5); // overwrites 2

        // then - oldest items (1, 2) should be overwritten
        var array = _ToArray(buffer);
        array.Should().Equal([3, 4, 5]);
    }

    [Fact]
    public void should_support_enumeration()
    {
        // given
        const int capacity = 5;
        var buffer = _CreateBuffer(capacity);
        _Add(buffer, 10);
        _Add(buffer, 20);
        _Add(buffer, 30);

        // when
        var enumerable = (IEnumerable<int>)buffer;
        var list = enumerable.ToList();

        // then
        list.Should().Equal([10, 20, 30]);
    }

    [Fact]
    public void should_index_correctly_after_wrap()
    {
        // given
        const int capacity = 3;
        var buffer = _CreateBuffer(capacity);

        // when - add more items than capacity to cause wrap
        _Add(buffer, 1);
        _Add(buffer, 2);
        _Add(buffer, 3);
        _Add(buffer, 4); // overwrites 1
        _Add(buffer, 5); // overwrites 2

        // then - indexer should return items in correct order
        _GetItem(buffer, 0).Should().Be(3);
        _GetItem(buffer, 1).Should().Be(4);
        _GetItem(buffer, 2).Should().Be(5);
    }

    [Fact]
    public void should_handle_zero_capacity()
    {
        // given
        const int capacity = 0;
        var buffer = _CreateBuffer(capacity);

        // when
        _Add(buffer, 1);
        _Add(buffer, 2);

        // then
        _GetCount(buffer).Should().Be(0);
        _GetCapacity(buffer).Should().Be(0);
    }

    [Fact]
    public void should_clear_buffer()
    {
        // given
        const int capacity = 5;
        var buffer = _CreateBuffer(capacity);
        _Add(buffer, 1);
        _Add(buffer, 2);
        _Add(buffer, 3);

        // when
        _Clear(buffer);

        // then
        _GetCount(buffer).Should().Be(0);
        _IsFull(buffer).Should().BeFalse();
    }

    [Fact]
    public void should_return_correct_count_before_full()
    {
        // given
        const int capacity = 5;
        var buffer = _CreateBuffer(capacity);

        // when
        _Add(buffer, 1);
        _Add(buffer, 2);

        // then
        _GetCount(buffer).Should().Be(2);
        _IsFull(buffer).Should().BeFalse();
    }

    [Fact]
    public void should_throw_on_negative_capacity()
    {
        // given & when
        var act = () => _CreateBuffer(-1);

        // then
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_on_invalid_index()
    {
        // given
        const int capacity = 3;
        var buffer = _CreateBuffer(capacity);
        _Add(buffer, 1);
        _Add(buffer, 2);

        // when
        var act = () => _GetItem(buffer, 5);

        // then
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_copy_to_array_correctly()
    {
        // given
        const int capacity = 3;
        var buffer = _CreateBuffer(capacity);
        _Add(buffer, 1);
        _Add(buffer, 2);
        _Add(buffer, 3);
        _Add(buffer, 4); // wrap

        var destArray = new int[3];

        // when
        buffer.GetType().GetMethod("CopyTo")!.Invoke(buffer, [destArray, 0]);

        // then
        destArray.Should().Equal([2, 3, 4]);
    }
}
