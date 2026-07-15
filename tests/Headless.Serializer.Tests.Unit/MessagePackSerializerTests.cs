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

    public sealed class DateTimeOffsetContainer
    {
        public required DateTimeOffset Timestamp { get; init; }
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
    public void should_write_to_buffer_when_serialize_valid_object()
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
    public void should_write_to_buffer_when_serialize_object_typed_value()
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
    public void should_throw_exception_when_serialize_non_serializable_object()
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
    public void should_return_object_when_roundtrip_via_bytes()
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
    public void should_return_object_when_roundtrip_via_buffer_and_memory()
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
    public void should_return_object_when_deserialize_from_read_only_sequence()
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
    public void should_throw_exception_when_deserialize_invalid_data()
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

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void should_round_trip_a_datetime_to_the_same_instant_expressed_as_utc(DateTimeKind kind)
    {
        // given
        // MessagePack's native Timestamp format stores an instant and hands it back as UTC; the original
        // DateTimeKind is NOT carried on the wire. Assert that contract explicitly, because
        // `Should().Be(...)` alone cannot: DateTime.Equals compares Ticks and IGNORES Kind, so a test written
        // that way passes even when the serializer has silently changed the kind.
        var timestamp = DateTime.SpecifyKind(new DateTime(2025, 6, 15, 10, 30, 45), kind);
        var container = new DateTimeContainer { Timestamp = timestamp };

        // when
        var bytes = _serializer.SerializeToBytes(container);
        var result = _serializer.Deserialize<DateTimeContainer>(bytes!);

        // then
        result.Should().NotBeNull();

        // Whatever kind went in, UTC comes back — the wire format carries an instant, not a kind.
        result.Timestamp.Kind.Should().Be(DateTimeKind.Utc);

        // MessagePack's kind handling is exactly the framework's NormalizeToUtc contract (spelled out here
        // rather than called, so this test does not depend on Headless.Extensions):
        //   Utc         -> unchanged
        //   Local       -> converted
        //   Unspecified -> ASSUMED to already be UTC and stamped in place (NOT converted)
        // That last arm is deliberately NOT DateTime.ToUniversalTime(), which interprets Unspecified as LOCAL
        // and would shift the value by the host's UTC offset.
        var expected = kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        };

        result.Timestamp.Should().Be(expected);
    }

    [Fact]
    public void should_round_trip_a_datetimeoffset_preserving_the_instant()
    {
        // given — a non-zero offset, so a serializer that drops the offset instead of converting is caught.
        var instant = new DateTimeOffset(2025, 6, 15, 13, 30, 45, TimeSpan.FromHours(3));
        var container = new DateTimeOffsetContainer { Timestamp = instant };

        // when
        var bytes = _serializer.SerializeToBytes(container);
        var result = _serializer.Deserialize<DateTimeOffsetContainer>(bytes!);

        // then — the same moment in time must survive, whatever offset it is expressed in.
        result.Should().NotBeNull();
        result.Timestamp.ToUniversalTime().Should().Be(instant.ToUniversalTime());
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
    public void should_use_untrusted_security_by_default_when_parameterless_serializer()
    {
        // given
        var serializer = new MessagePackSerializer();

        // when
        var options = _ReadOptions(serializer);

        // then
        options.Security.Should().BeSameAs(MessagePackSecurity.UntrustedData);
    }

    [Fact]
    public void should_use_trusted_security_when_trusted_data_opt_out()
    {
        // given
        var serializer = new MessagePackSerializer(untrustedData: false);

        // when
        var options = _ReadOptions(serializer);

        // then
        options.Security.Should().BeSameAs(MessagePackSecurity.TrustedData);
    }

    [Fact]
    public void should_own_security_level_when_supplied_options()
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
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
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
