// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;

namespace Tests;

public sealed class SerializerExtensionsTests
{
    private readonly ISerializer _serializer = Substitute.For<ISerializer>();
    private readonly ITextSerializer _textSerializer = Substitute.For<ITextSerializer>();

    [Fact]
    public void should_serialize_to_bytes()
    {
        // given
        var obj = new TestClass { Name = "Test", Value = 42 };
        _serializer
            .When(s => s.Serialize(Arg.Any<TestClass>(), Arg.Any<Stream>()))
            .Do(c =>
            {
                var stream = c.Arg<Stream>();
                var bytes = Encoding.UTF8.GetBytes("{\"name\":\"Test\",\"value\":42}");
                stream.Write(bytes);
            });

        // when
        var result = _serializer.SerializeToBytes(obj);

        // then
        result.Should().NotBeNull();
        Encoding.UTF8.GetString(result!).Should().Be("{\"name\":\"Test\",\"value\":42}");
    }

    [Fact]
    public void should_serialize_to_bytes_return_null_when_value_is_null()
    {
        // when
        var result = _serializer.SerializeToBytes<TestClass>(null);

        // then
        result.Should().BeNull();
        _serializer.DidNotReceive().Serialize(Arg.Any<TestClass>(), Arg.Any<Stream>());
    }

    [Fact]
    public void should_deserialize_from_bytes()
    {
        // given
        var expected = new TestClass { Name = "Test", Value = 42 };
        var bytes = Encoding.UTF8.GetBytes("{\"name\":\"Test\",\"value\":42}");
        _serializer.Deserialize<TestClass>(Arg.Any<Stream>()).Returns(expected);

        // when
        var result = _serializer.Deserialize<TestClass>(bytes);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_serialize_to_string_for_text_serializer()
    {
        // given
        var obj = new TestClass { Name = "Test", Value = 42 };
        _textSerializer
            .When(s => s.Serialize(Arg.Any<TestClass>(), Arg.Any<Stream>()))
            .Do(c =>
            {
                var stream = c.Arg<Stream>();
                var bytes = Encoding.UTF8.GetBytes("{\"name\":\"Test\",\"value\":42}");
                stream.Write(bytes);
            });

        // when
        var result = _textSerializer.SerializeToString(obj);

        // then
        result.Should().NotBeNull();
        result.Should().Be("{\"name\":\"Test\",\"value\":42}");
    }

    [Fact]
    public void should_serialize_to_string_return_null_when_value_is_null()
    {
        // when
        var result = _textSerializer.SerializeToString<TestClass>(null);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_serialize_to_string_as_base64_for_binary_serializer()
    {
        // given
        var obj = new TestClass { Name = "Test", Value = 42 };
        byte[] binaryBytes = [0x01, 0x02, 0x03, 0x04];
        _serializer
            .When(s => s.Serialize(Arg.Any<TestClass>(), Arg.Any<Stream>()))
            .Do(c =>
            {
                var stream = c.Arg<Stream>();
                stream.Write(binaryBytes);
            });

        // when
        var result = _serializer.SerializeToString(obj);

        // then
        result.Should().NotBeNull();
        result.Should().Be(Convert.ToBase64String(binaryBytes));
    }

    [Fact]
    public void should_deserialize_from_string_for_text_serializer()
    {
        // given
        const string json = "{\"name\":\"Test\",\"value\":42}";
        var expected = new TestClass { Name = "Test", Value = 42 };
        _textSerializer.Deserialize<TestClass>(Arg.Any<Stream>()).Returns(expected);

        // when
        var result = _textSerializer.Deserialize<TestClass>(json);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_deserialize_from_string_with_base64_for_binary_serializer()
    {
        // given
        var binaryBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var base64 = Convert.ToBase64String(binaryBytes);
        var expected = new TestClass { Name = "Test", Value = 42 };
        _serializer.Deserialize<TestClass>(Arg.Any<Stream>()).Returns(expected);

        // when
        var result = _serializer.Deserialize<TestClass>(base64);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public void should_deserialize_from_null_string()
    {
        // given
        _serializer.Deserialize<TestClass>(Arg.Any<Stream>()).Returns((TestClass?)null);

        // when
        var result = _serializer.Deserialize<TestClass>((string?)null);

        // then
        _serializer.Received(1).Deserialize<TestClass>(Arg.Any<Stream>());
    }

    private sealed class TestClass
    {
        public required string Name { get; init; }

        public required int Value { get; init; }
    }
}
