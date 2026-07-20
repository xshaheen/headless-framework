// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests;

public sealed class JobsHelperTests
{
    private static JobsRequestSerializationOptions _Options(bool compressed)
    {
        return new JobsRequestSerializationOptions { UseGZipCompression = compressed };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_round_trip_typed_and_textual_legacy_payloads(bool compressed)
    {
        var options = _Options(compressed);
        var request = new SampleRequest("payload", 42);

        var bytes = JobsHelper.CreateJobRequest(request, options);

        JobsHelper.ReadJobRequest<SampleRequest>(bytes, options).Should().Be(request);
        JobsHelper.ReadJobRequestAsString(bytes, options).Should().Be("{\"Name\":\"payload\",\"Value\":42}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_return_null_for_json_null(bool compressed)
    {
        var options = _Options(compressed);
        var bytes = JobsHelper.CreateJobRequest(Encoding.UTF8.GetBytes("null"), options);

        JobsHelper.ReadJobRequest<SampleRequest>(bytes, options).Should().BeNull();
    }

    [Fact]
    public void should_reject_empty_or_malformed_plain_json()
    {
        var options = _Options(compressed: false);

        var readEmpty = () => JobsHelper.ReadJobRequest<SampleRequest>([], options);
        var readMalformed = () => JobsHelper.ReadJobRequest<SampleRequest>("{"u8.ToArray(), options);

        readEmpty.Should().Throw<JsonException>();
        readMalformed.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_missing_sentinel_or_truncated_compressed_json()
    {
        var options = _Options(compressed: true);

        var readMissing = () => JobsHelper.ReadJobRequest<SampleRequest>([0x1f, 0x8b, 0x08], options);
        var readTruncated = () =>
            JobsHelper.ReadJobRequest<SampleRequest>([0x1f, 0x8b, 0x08, 0x00, 0x1f, 0x8b, 0x08, 0x00], options);

        readMissing.Should().Throw<InvalidOperationException>();
        readTruncated.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_only_recognize_the_trailing_signature()
    {
        var options = _Options(compressed: true);
        byte[] bytes = [0x1f, 0x8b, 0x08, 0x00, 0x01];

        var read = () => JobsHelper.ReadJobRequest<SampleRequest>(bytes, options);

        read.Should().Throw<InvalidOperationException>();
    }

    private sealed record SampleRequest(string Name, int Value);
}
