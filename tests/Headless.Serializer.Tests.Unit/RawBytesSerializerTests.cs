// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;

namespace Tests;

public sealed class RawBytesSerializerTests
{
    private readonly RawBytesSerializer _serializer = new();

    [Fact]
    public void should_round_trip_bytes_without_changing_length_or_content()
    {
        // given
        var bytes = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var serialized = output.ToArray();
        var roundTripped = _serializer.Deserialize<byte[]>(new MemoryStream(serialized));

        // then
        serialized.Should().HaveCount(bytes.Length);
        serialized.Should().Equal(bytes);
        roundTripped.Should().Equal(bytes);
    }

    [Fact]
    public void should_round_trip_empty_bytes()
    {
        // given
        var bytes = Array.Empty<byte>();
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var roundTripped = _serializer.Deserialize<byte[]>(new MemoryStream(output.ToArray()));

        // then
        output.ToArray().Should().BeEmpty();
        roundTripped.Should().BeEmpty();
    }

    [Fact]
    public void should_support_object_overloads_for_bytes()
    {
        // given
        object bytes = new byte[] { 1, 2, 3, 4 };
        using var output = new MemoryStream();

        // when
        _serializer.Serialize(bytes, output);
        var roundTripped = _serializer.Deserialize(new MemoryStream(output.ToArray()), typeof(byte[]));

        // then
        roundTripped.Should().BeOfType<byte[]>().Which.Should().Equal((byte[])bytes);
    }

    [Fact]
    public void should_reject_non_byte_array_values()
    {
        // given
        using var output = new MemoryStream();

        // when
        var serialize = () => _serializer.Serialize("value", output);
        var deserialize = () => _serializer.Deserialize<string>(new MemoryStream([1, 2, 3]));

        // then
        serialize.Should().Throw<NotSupportedException>().WithMessage("*byte[]*String*");
        deserialize.Should().Throw<NotSupportedException>().WithMessage("*byte[]*String*");
    }

    [Fact]
    public void should_reject_null_byte_array()
    {
        // given
        byte[]? bytes = null;
        using var output = new MemoryStream();

        // when
        var action = () => _serializer.Serialize(bytes, output);

        // then
        action.Should().Throw<ArgumentNullException>();
    }
}
