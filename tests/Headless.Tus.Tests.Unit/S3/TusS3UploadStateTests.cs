// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Testing.Tests;
using Headless.Tus.Internal;

namespace Tests.S3;

public sealed class TusS3UploadStateTests : TestBase
{
    [Fact]
    public void should_round_trip_every_field()
    {
        // given
        var expires = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var state = new TusS3UploadState
        {
            PartSize = 8 * 1024 * 1024,
            UploadLength = 20_000_000,
            Metadata = "filename dGVzdC5iaW4=,empty",
            Concat = new TusS3ConcatInfo(TusS3ConcatKind.Final, ["a", "b"]),
            ExpiresAt = expires,
            MultipartUploadId = "mpu-1",
            MultipartUploadCreatedAt = expires.AddDays(-1),
            Parts = [new TusS3CommittedPart(1, "\"etag-1\"", 8 * 1024 * 1024)],
            Segments = [new TusS3CommittedSegment("uploads/.tus/x/seg/8388608-n", 8_388_608, 100)],
            NextPartNumber = 65,
            Offset = 8_388_708,
            Status = TusS3UploadStatus.Completing,
            PreFinalOffset = 8_388_608,
            CompletingFromDeclaredLength = true,
            ExpiresAtBeforeCompleting = expires.AddHours(-1),
            SlidingExpirationWindow = TimeSpan.FromMinutes(30),
        };

        // when
        var read = TusS3UploadState.FromJson(state.ToJson());

        // then
        read.Should().BeEquivalentTo(state);
    }

    [Fact]
    public void should_write_the_schema_version_and_status_by_name()
    {
        // given
        var state = new TusS3UploadState { PartSize = 5 * 1024 * 1024, Status = TusS3UploadStatus.Gone };

        // when
        var json = JsonNode.Parse(state.ToJson())!;

        // then - the status name survives a reorder of the enum; the version lets a reader refuse a newer schema
        json["schemaVersion"]!.GetValue<int>().Should().Be(1);
        json["status"]!.GetValue<string>().Should().Be("Gone");
    }

    [Fact]
    public void should_keep_a_deferred_length_as_null()
    {
        var read = TusS3UploadState.FromJson(new TusS3UploadState { PartSize = 1 }.ToJson());

        read.UploadLength.Should().BeNull();
        read.Status.Should().Be(TusS3UploadStatus.Active);
        read.NextPartNumber.Should().Be(1);
        read.Parts.Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_state_written_by_a_newer_schema()
    {
        // given
        var json = """{"schemaVersion":2,"partSize":5242880}"""u8.ToArray();

        // when
        var act = () => TusS3UploadState.FromJson(json);

        // then
        act.Should().Throw<InvalidDataException>().WithMessage("*schema version 2*");
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"partSize":""")]
    [InlineData("""{"schemaVersion":1,"partSize":5242880,"status":"Paused"}""")]
    [InlineData("""{"schemaVersion":1,"partSize":"many"}""")]
    public void should_report_a_state_it_cannot_parse_as_invalid_data(string json)
    {
        // when
        var act = () => TusS3UploadState.FromJson(Encoding.UTF8.GetBytes(json));

        // then - the sweep skips invalid data, so a corrupt object must not surface as any other exception
        act.Should().Throw<InvalidDataException>().WithInnerException<JsonException>();
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"uploadLength":""")]
    [InlineData("""{"schemaVersion":1,"uploadLength":1,"concat":{"kind":"Middle"}}""")]
    public void should_report_a_completed_record_it_cannot_parse_as_invalid_data(string json)
    {
        // when
        var act = () => TusS3CompletedRecord.FromJson(Encoding.UTF8.GetBytes(json));

        // then
        act.Should().Throw<InvalidDataException>().WithInnerException<JsonException>();
    }

    [Fact]
    public void should_round_trip_a_completed_record()
    {
        // given
        var record = new TusS3CompletedRecord
        {
            UploadLength = 0,
            Metadata = "filename dGVzdC5iaW4=",
            Concat = new TusS3ConcatInfo(TusS3ConcatKind.Partial),
        };

        // when
        var read = TusS3CompletedRecord.FromJson(record.ToJson());

        // then
        read.Should().BeEquivalentTo(record);
    }
}
