// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

public sealed class TransportBodyTests : TestBase
{
    [Fact]
    public void should_return_the_backing_array_when_the_body_spans_the_whole_array()
    {
        // given
        var array = new byte[] { 1, 2, 3 };

        // when
        var result = TransportBody.ToArray(array);

        // then
        result.Should().BeSameAs(array);
    }

    [Fact]
    public void should_copy_the_slice_when_the_body_starts_past_the_array_start()
    {
        // given
        var array = new byte[] { 1, 2, 3, 4 };

        // when
        var result = TransportBody.ToArray(new ReadOnlyMemory<byte>(array, 1, 3));

        // then
        result.Should().Equal(2, 3, 4);
        result.Should().NotBeSameAs(array);
    }

    [Fact]
    public void should_copy_the_slice_when_the_body_ends_before_the_array_end()
    {
        // given
        var array = new byte[] { 1, 2, 3, 4 };

        // when
        var result = TransportBody.ToArray(new ReadOnlyMemory<byte>(array, 0, 2));

        // then
        result.Should().Equal(1, 2);
        result.Should().NotBeSameAs(array);
    }

    [Fact]
    public void should_return_an_empty_array_when_the_body_is_empty()
    {
        // when
        var result = TransportBody.ToArray(ReadOnlyMemory<byte>.Empty);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public void should_copy_the_bytes_when_the_body_is_not_backed_by_an_array()
    {
        // given
        using var manager = new NativeLikeMemoryManager([5, 6, 7]);

        // when
        var result = TransportBody.ToArray(manager.Memory);

        // then
        result.Should().Equal(5, 6, 7);
    }

    // Exposes memory that MemoryMarshal.TryGetArray cannot map to an array, as native or pooled memory would.
    private sealed class NativeLikeMemoryManager(byte[] data) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => data;

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }
}
