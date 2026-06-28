// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;

namespace Tests;

public sealed class IdempotencyRecordSerializationTests
{
    private static readonly JsonSerializerOptions _Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void complete_record_round_trips_via_json()
    {
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 201,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
                ["X-Custom"] = ["a", "b"],
            },
            Body = [1, 2, 3, 4, 5],
            Fingerprint = [0xAA, 0xBB, 0xCC],
            CreatedAt = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Kind.Should().Be(RecordKind.Complete);
        restored.StatusCode.Should().Be(201);
        restored.Headers["Content-Type"].Should().Equal("application/json");
        restored.Headers["X-Custom"].Should().Equal("a", "b");
        restored.Body.Should().Equal(original.Body);
        restored.Fingerprint.Should().Equal(original.Fingerprint);
        restored.CreatedAt.Should().Be(original.CreatedAt);
    }

    [Fact]
    public void in_flight_marker_round_trips_via_json()
    {
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = [0x01, 0x02],
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Kind.Should().Be(RecordKind.InFlight);
        restored.Body.Should().BeEmpty();
        restored.Fingerprint.Should().Equal(original.Fingerprint);
    }

    [Fact]
    public void null_fingerprint_round_trips_via_json()
    {
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.InFlight,
            Fingerprint = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Fingerprint.Should().BeNull();
    }

    [Fact]
    public void headers_with_empty_value_array_round_trips_via_json()
    {
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Empty"] = [],
                ["X-Single"] = ["one"],
                ["X-Multi"] = ["a", "b", "c"],
            },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Headers["X-Empty"].Should().BeEmpty();
        restored.Headers["X-Single"].Should().Equal("one");
        restored.Headers["X-Multi"].Should().Equal("a", "b", "c");
    }

    [Fact]
    public void body_with_all_256_byte_values_round_trips_via_json()
    {
        var allBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = allBytes,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Body.Should().Equal(allBytes);
    }

    [Fact]
    public void headers_lookups_remain_case_insensitive_after_round_trip()
    {
        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
                ["Cache-Control"] = ["no-store"],
            },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        // Lowercase keys must hit the case-insensitive comparer.
        restored.Headers["content-type"].Should().Equal("application/json");
        restored.Headers["CACHE-CONTROL"].Should().Equal("no-store");
        restored.Headers.ContainsKey("Content-Type").Should().BeTrue();
        restored.Headers.ContainsKey("content-TYPE").Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    public void body_of_various_sizes_round_trips_via_json(int size)
    {
        var body = new byte[size];
        new Random(42).NextBytes(body);

        var original = new IdempotencyRecord
        {
            Kind = RecordKind.Complete,
            StatusCode = 200,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, _Options);
        var restored = JsonSerializer.Deserialize<IdempotencyRecord>(json, _Options)!;

        restored.Body.Should().Equal(body);
    }
}
