// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Reflection;
using Headless.Serializer;
using MessagePack;
using MessagePackSerializer = Headless.Serializer.MessagePackSerializer;

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
    public void serialize_valid_object_should_write_to_buffer()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };
        var writer = new ArrayBufferWriter<byte>();

        // when
        _serializer.Serialize(person, writer);

        // then
        writer.WrittenSpan.ToArray().Should().NotBeEmpty();
    }

    [Fact]
    public void serialize_object_typed_value_should_write_to_buffer()
    {
        // given
        var person = new Person { Name = "Bob", Age = 40 };
        var writer = new ArrayBufferWriter<byte>();

        // when
        _serializer.Serialize((object)person, writer);

        // then
        writer.WrittenSpan.ToArray().Should().NotBeEmpty();
    }

    [Fact]
    public void serialize_non_serializable_object_should_throw_exception()
    {
        // given
        var nonSerializable = new NonSerializable { Action = () => Console.WriteLine("Test") };
        var writer = new ArrayBufferWriter<byte>();

        // when
        var act = () => _serializer.Serialize(nonSerializable, writer);

        // then
        act.Should().Throw<MessagePackSerializationException>();
    }

    [Fact]
    public void roundtrip_via_bytes_should_return_object()
    {
        // given
        var person = new Person { Name = "Alice", Age = 30 };

        // when
        var bytes = _serializer.SerializeToBytes(person);
        var result = _serializer.Deserialize<Person>(bytes!);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Alice");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void roundtrip_via_buffer_and_memory_should_return_object()
    {
        // given
        var person = new Person { Name = "Bob", Age = 40 };
        var writer = new ArrayBufferWriter<byte>();

        // when
        _serializer.Serialize(person, writer);
        var result = _serializer.Deserialize<Person>(writer.WrittenMemory);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Bob");
        result.Age.Should().Be(40);
    }

    [Fact]
    public void deserialize_from_read_only_sequence_should_return_object()
    {
        // given — a multi-segment sequence exercises the non-contiguous read path.
        var person = new Person { Name = "Seq", Age = 7 };
        var bytes = _serializer.SerializeToBytes(person)!;
        var sequence = _CreateMultiSegmentSequence(bytes);

        // when
        var result = _serializer.Deserialize<Person>(in sequence);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Seq");
        result.Age.Should().Be(7);
    }

    [Fact]
    public void deserialize_invalid_data_should_throw_exception()
    {
        // given
        byte[] invalid = [0x01, 0x02, 0x03];

        // when
        var act = () => _serializer.Deserialize<Person>(invalid);

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

        // when
        var bytes = _serializer.SerializeToBytes(complex);
        var result = _serializer.Deserialize<ComplexObject>(bytes!);

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
        var writer = new ArrayBufferWriter<byte>();

        // when
        _serializer.Serialize(nullPerson, writer);
        var result = _serializer.Deserialize<Person>(writer.WrittenMemory);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_be_smaller_than_json()
    {
        // given
        var person = new Person { Name = "Test Person With A Longer Name", Age = 12345 };

        // when
        var msgpackSize = _serializer.SerializeToBytes(person)!.Length;
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

        // when
        var bytes = _serializer.SerializeToBytes(container);
        var result = _serializer.Deserialize<DateTimeContainer>(bytes!);

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

        // when
        var bytes = _serializer.SerializeToBytes(container);
        var result = _serializer.Deserialize<GuidContainer>(bytes!);

        // then
        result.Should().NotBeNull();
        result.Id.Should().Be(guid);
    }

    [Fact]
    public void untrusted_data_serializer_roundtrips()
    {
        // given — the untrustedData default hardens deserialization; it must not change round-trip correctness.
        var serializer = new MessagePackSerializer(untrustedData: true);
        var person = new Person { Name = "Trusted", Age = 21 };

        // when
        var bytes = serializer.SerializeToBytes(person);
        var result = serializer.Deserialize<Person>(bytes!);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Trusted");
        result.Age.Should().Be(21);
    }

    [Fact]
    public void parameterless_serializer_should_use_untrusted_security_by_default()
    {
        // given
        var serializer = new MessagePackSerializer();

        // when
        var options = _ReadOptions(serializer);

        // then
        options.Security.Should().BeSameAs(MessagePackSecurity.UntrustedData);
    }

    [Fact]
    public void trusted_data_opt_out_should_use_trusted_security()
    {
        // given
        var serializer = new MessagePackSerializer(untrustedData: false);

        // when
        var options = _ReadOptions(serializer);

        // then
        options.Security.Should().BeSameAs(MessagePackSecurity.TrustedData);
    }

    [Fact]
    public void supplied_options_should_own_security_level()
    {
        // given
        var suppliedOptions = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.TrustedData);

        // when
        var serializer = new MessagePackSerializer(suppliedOptions);
        var resolvedOptions = _ReadOptions(serializer);

        // then
        resolvedOptions.Should().BeSameAs(suppliedOptions);
    }

    [Fact]
    public void should_deserialize_with_type_parameter()
    {
        // given
        var person = new Person { Name = "TypeTest", Age = 99 };
        var bytes = _serializer.SerializeToBytes(person)!;

        // when
#pragma warning disable CA2263 // Prefer generic
        var result = _serializer.Deserialize<Person>(bytes.AsMemory());
#pragma warning restore CA2263

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<Person>();
        result.Name.Should().Be("TypeTest");
        result.Age.Should().Be(99);
    }

    // Splits the payload across two segments so deserialization runs against a genuinely non-contiguous sequence.
    private static ReadOnlySequence<byte> _CreateMultiSegmentSequence(byte[] data)
    {
        if (data.Length < 2)
        {
            return new ReadOnlySequence<byte>(data);
        }

        var mid = data.Length / 2;
        var first = new BufferSegment(data.AsMemory(0, mid));
        var second = first.Append(data.AsMemory(mid));

        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private static MessagePackSerializerOptions _ReadOptions(MessagePackSerializer serializer)
    {
        var field = typeof(MessagePackSerializer).GetField(
            "_options",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        return field?.GetValue(serializer) as MessagePackSerializerOptions
            ?? throw new InvalidOperationException("Unable to read resolved MessagePack serializer options.");
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;

            return segment;
        }
    }
}
