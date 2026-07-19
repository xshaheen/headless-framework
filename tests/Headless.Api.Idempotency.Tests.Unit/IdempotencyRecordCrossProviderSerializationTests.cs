// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Serializer;
using MessagePack;
using MessagePack.Resolvers;

namespace Tests;

/// <summary>
/// Pins the cross-provider serialization contract for <see cref="IdempotencyRecord"/>. The Redis
/// cache provider serializes through <see cref="Headless.Serializer.ISerializer"/>; the default
/// binding is <see cref="SystemJsonSerializer"/> (System.Text.Json under the hood). Consumers may
/// swap in <see cref="MessagePackBinarySerializer"/> for a more compact wire format. Both paths must
/// round-trip the fields the middleware actually reads at replay time: <c>StatusCode</c>,
/// <c>Body</c> (exact byte equality), <c>Fingerprint</c> (exact byte equality including null),
/// <c>Kind</c>, <c>CreatedAt</c>, and <c>Headers</c> entries (key/value pairs preserved by
/// iteration — comparer preservation is a JSON-only guarantee).
/// </summary>
public sealed class IdempotencyRecordCrossProviderSerializationTests
{
    private static IdempotencyRecord _Sample()
    {
        return new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
                ["Cache-Control"] = ["no-store"],
                ["X-Custom"] = ["a", "b"],
            },
            Body = [.. Enumerable.Range(0, 256).Select(i => (byte)i)],
            Fingerprint = [0xDE, 0xAD, 0xBE, 0xEF],
            CreatedAt = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
        };
    }

    // ── ISerializer (default Redis path: SystemJsonSerializer) ──────────────────

    [Fact]
    public void complete_record_round_trips_via_system_json_serializer()
    {
        var serializer = new SystemJsonSerializer();
        var original = _Sample();

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Kind.Should().Be(RecordKind.Complete);
        restored.StatusCode.Should().Be(201);
        restored.Body.Should().Equal(original.Body);
        restored.Fingerprint.Should().Equal(original.Fingerprint);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        restored.Headers.Should().ContainKey("Content-Type").WhoseValue.Should().Equal("application/json");
        restored.Headers.Should().ContainKey("Cache-Control").WhoseValue.Should().Equal("no-store");
        restored.Headers.Should().ContainKey("X-Custom").WhoseValue.Should().Equal("a", "b");
    }

    [Fact]
    public void system_json_serializer_preserves_ordinal_ignore_case_comparer_on_headers()
    {
        // The custom OrdinalIgnoreCaseHeadersJsonConverter is the contract pin: a consumer that
        // reads record.Headers["content-type"] after a cache miss-then-hit must hit the same
        // case-insensitive comparer as at capture time.
        var serializer = new SystemJsonSerializer();
        var original = _Sample();

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Headers["content-type"].Should().Equal("application/json");
        restored.Headers["CACHE-CONTROL"].Should().Equal("no-store");
        restored.Headers.Should().ContainKey("content-TYPE");
    }

    [Fact]
    public void in_flight_marker_round_trips_via_system_json_serializer()
    {
        var serializer = new SystemJsonSerializer();
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = [0x01, 0x02, 0x03],
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Kind.Should().Be(RecordKind.InFlight);
        restored.Fingerprint.Should().Equal(original.Fingerprint);
        restored.Body.Should().BeEmpty();
    }

    [Fact]
    public void null_fingerprint_round_trips_via_system_json_serializer()
    {
        var serializer = new SystemJsonSerializer();
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Fingerprint.Should().BeNull();
    }

    // ── MessagePack (consumer-opt-in alternative) ───────────────────────────────

    [Fact]
    public void complete_record_round_trips_via_message_pack_serializer()
    {
        // IdempotencyRecord is internal sealed (project API discipline). MessagePack's default
        // ContractlessStandardResolver only supports public types, so consumers swapping in
        // MessagePack must configure the AllowPrivate variant. The package README documents
        // this requirement.
        var serializer = new MessagePackBinarySerializer(
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        );
        var original = _Sample();

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Kind.Should().Be(RecordKind.Complete);
        restored.StatusCode.Should().Be(201);
        restored.Body.Should().Equal(original.Body);
        restored.Fingerprint.Should().Equal(original.Fingerprint);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        // Headers content survives the round-trip even though MessagePack's default
        // resolver rebuilds the dictionary with the default ordinal comparer (the middleware
        // iterates record.Headers and never does a case-insensitive lookup, so the
        // comparer loss does not affect replay correctness).
        restored.Headers.Should().HaveCount(3);
        restored.Headers["Content-Type"].Should().Equal("application/json");
        restored.Headers["Cache-Control"].Should().Equal("no-store");
        restored.Headers["X-Custom"].Should().Equal("a", "b");
    }

    [Fact]
    public void in_flight_marker_round_trips_via_message_pack_serializer()
    {
        // IdempotencyRecord is internal sealed (project API discipline). MessagePack's default
        // ContractlessStandardResolver only supports public types, so consumers swapping in
        // MessagePack must configure the AllowPrivate variant. The package README documents
        // this requirement.
        var serializer = new MessagePackBinarySerializer(
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        );
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = [0x01, 0x02, 0x03],
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Kind.Should().Be(RecordKind.InFlight);
        restored.Fingerprint.Should().Equal(original.Fingerprint);
        restored.Body.Should().BeEmpty();
    }

    [Fact]
    public void null_fingerprint_round_trips_via_message_pack_serializer()
    {
        // IdempotencyRecord is internal sealed (project API discipline). MessagePack's default
        // ContractlessStandardResolver only supports public types, so consumers swapping in
        // MessagePack must configure the AllowPrivate variant. The package README documents
        // this requirement.
        var serializer = new MessagePackBinarySerializer(
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        );
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Fingerprint.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(64 * 1024)]
    public void body_of_various_sizes_round_trips_via_message_pack_serializer(int size)
    {
        var body = new byte[size];
        new Random(42).NextBytes(body);

        // IdempotencyRecord is internal sealed (project API discipline). MessagePack's default
        // ContractlessStandardResolver only supports public types, so consumers swapping in
        // MessagePack must configure the AllowPrivate variant. The package README documents
        // this requirement.
        var serializer = new MessagePackBinarySerializer(
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        );
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using var buffer = new MemoryStream();
        serializer.Serialize(original, buffer);
        buffer.Position = 0;
        var restored = serializer.Deserialize<IdempotencyRecord>(buffer)!;

        restored.Body.Should().Equal(body);
    }
}
