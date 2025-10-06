using MessagePack;
using MessagePackSerializer = Framework.Serializer.MessagePackSerializer;

// ReSharper disable AccessToDisposedClosure

namespace Tests;

public class MessagePackSerializerTests
{
    private readonly MessagePackSerializer _serializer = new();

    public class Person
    {
        public required string Name { get; init; }

        public int Age { get; init; }
    }

    public class NonSerializable
    {
        public required Action Action { get; init; }
    }

    [Fact]
    public void serialize_valid_object_should_write_to_stream()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize(person, memoryStream);

        // then
        memoryStream.ToArray().Should().NotBeEmpty();
    }

    [Fact]
    public void serialize_valid_object_value_should_write_to_stream()
    {
        // given
        var person = new Person { Name = "Bob", Age = 40 };
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize((object)person, memoryStream);

        // then
        memoryStream.ToArray().Should().NotBeEmpty();
    }

    [Fact]
    public void serialize_to_closed_stream_should_throw_exception()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        using var memoryStream = new MemoryStream();
        memoryStream.Close();

        // when
        var act = () => _serializer.Serialize(person, memoryStream);

        // then
        act.Should().Throw<MessagePackSerializationException>();
    }

    [Fact]
    public void serialize_non_serializable_object_should_throw_exception()
    {
        // given
        var nonSerializable = new NonSerializable { Action = () => Console.WriteLine("Test") };
        using var memoryStream = new MemoryStream();

        // when
        var act = () => _serializer.Serialize(nonSerializable, memoryStream);

        // then
        act.Should().Throw<MessagePackSerializationException>();
    }

    [Fact]
    public void serialize_to_null_stream_should_throw_exception()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        Stream? nullStream = null;

        // when
        var act = () => _serializer.Serialize(person, nullStream!);

        // then
        act.Should().Throw<MessagePackSerializationException>();
    }

    [Fact]
    public void deserialize_valid_steam_should_return_object()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        using var memoryStream = new MemoryStream();
        _serializer.Serialize(person, memoryStream);
        memoryStream.Position = 0;

        // when
        var result = _serializer.Deserialize<Person>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Alice");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void deserialize_valid_generic_object_from_stream_should_return_object()
    {
        // given
        var person = new Person { Name = "Bob", Age = 40 };
        using var memoryStream = new MemoryStream();
        _serializer.Serialize(person, memoryStream);
        memoryStream.Position = 0;

        // when
        var result = _serializer.Deserialize<Person>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Bob");
        result.Age.Should().Be(40);
    }

    [Fact]
    public void deserialize_closed_stream_should_throw_exception()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        using var memoryStream = new MemoryStream();
        _serializer.Serialize(person, memoryStream);
        memoryStream.Close();

        // when
        Action act = () => _serializer.Deserialize<Person>(memoryStream);

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void deserialize_valid_typed_object_from_stream_should_return_object()
    {
        // given
        var person = new Person { Name = "Bob", Age = 40 };
        using var memoryStream = new MemoryStream();
        _serializer.Serialize(person, memoryStream);
        memoryStream.Position = 0;

        // when
        var result = _serializer.Deserialize<Person>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Bob");
        result.Age.Should().Be(40);
    }

    [Fact]
    public void deserialize_invalid_data_should_throw_exception()
    {
        // given
        using var memoryStream = new MemoryStream([0x01, 0x02, 0x03]);

        // when
        Action act = () => _serializer.Deserialize<Person>(memoryStream);

        // then
        act.Should().Throw<MessagePackSerializationException>();
    }
}
