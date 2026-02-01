using System.Text.Json;
using MessagePack;
using MessagePackSerializer = Headless.Serializer.MessagePackSerializer;

// ReSharper disable AccessToDisposedClosure

namespace Tests;

public sealed class MessagePackSerializerTests
{
    private readonly MessagePackSerializer _serializer = new();

    public sealed class Person
    {
        public required string Name { get; init; }

        public int Age { get; init; }
    }

    public sealed class ComplexObject
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required List<string> Tags { get; init; }

        public required Dictionary<string, int> Metadata { get; init; }

        public required NestedObject Nested { get; init; }
    }

    public sealed class NestedObject
    {
        public required string Value { get; init; }

        public int Count { get; init; }
    }

    public sealed class DateTimeContainer
    {
        public required DateTime Timestamp { get; init; }
    }

    public sealed class GuidContainer
    {
        public required Guid Id { get; init; }
    }

    public sealed class NonSerializable
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

    [Fact]
    public void should_serialize_and_deserialize_complex_object()
    {
        // given
        var complex = new ComplexObject
        {
            Id = "test-123",
            Name = "Test Object",
            Tags = ["tag1", "tag2", "tag3"],
            Metadata = new Dictionary<string, int>(StringComparer.Ordinal) { ["key1"] = 100, ["key2"] = 200 },
            Nested = new NestedObject { Value = "nested-value", Count = 42 },
        };
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize(complex, memoryStream);
        memoryStream.Position = 0;
        var result = _serializer.Deserialize<ComplexObject>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Id.Should().Be("test-123");
        result.Name.Should().Be("Test Object");
        result.Tags.Should().BeEquivalentTo(["tag1", "tag2", "tag3"]);
        result.Metadata.Should().ContainKey("key1").WhoseValue.Should().Be(100);
        result.Metadata.Should().ContainKey("key2").WhoseValue.Should().Be(200);
        result.Nested.Value.Should().Be("nested-value");
        result.Nested.Count.Should().Be(42);
    }

    [Fact]
    public void should_handle_null_value()
    {
        // given
        Person? nullPerson = null;
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize(nullPerson, memoryStream);
        memoryStream.Position = 0;
        var result = _serializer.Deserialize<Person>(memoryStream);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_be_smaller_than_json()
    {
        // given
        var person = new Person { Name = "Test Person With A Longer Name", Age = 12345 };
        using var msgpackStream = new MemoryStream();

        // when
        _serializer.Serialize(person, msgpackStream);
        var msgpackSize = msgpackStream.ToArray().Length;
        var jsonSize = JsonSerializer.SerializeToUtf8Bytes(person).Length;

        // then
        msgpackSize.Should().BeLessThan(jsonSize);
    }

    [Fact]
    public void should_handle_datetime()
    {
        // given
        var timestamp = new DateTime(2025, 6, 15, 10, 30, 45, DateTimeKind.Utc);
        var container = new DateTimeContainer { Timestamp = timestamp };
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize(container, memoryStream);
        memoryStream.Position = 0;
        var result = _serializer.Deserialize<DateTimeContainer>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void should_handle_guid()
    {
        // given
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var container = new GuidContainer { Id = guid };
        using var memoryStream = new MemoryStream();

        // when
        _serializer.Serialize(container, memoryStream);
        memoryStream.Position = 0;
        var result = _serializer.Deserialize<GuidContainer>(memoryStream);

        // then
        result.Should().NotBeNull();
        result.Id.Should().Be(guid);
    }

    [Fact]
    public void should_deserialize_with_type_parameter()
    {
        // given
        var person = new Person { Name = "TypeTest", Age = 99 };
        using var memoryStream = new MemoryStream();
        _serializer.Serialize(person, memoryStream);
        memoryStream.Position = 0;

        // when
        var result = _serializer.Deserialize(memoryStream, typeof(Person));

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<Person>();
        var typedResult = (Person)result!;
        typedResult.Name.Should().Be("TypeTest");
        typedResult.Age.Should().Be(99);
    }
}
