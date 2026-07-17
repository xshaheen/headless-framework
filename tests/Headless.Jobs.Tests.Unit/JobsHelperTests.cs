// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class JobsHelperTests : IDisposable
{
    private readonly JsonSerializerOptions _originalOptions = JobsHelper.RequestJsonSerializerOptions;
    private readonly bool _originalCompression = JobsHelper.UseGZipCompression;

    public void Dispose()
    {
        JobsHelper.RequestJsonSerializerOptions = _originalOptions;
        JobsHelper.UseGZipCompression = _originalCompression;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_round_trip_typed_and_textual_legacy_payloads(bool compressed)
    {
        JobsHelper.UseGZipCompression = compressed;
        var request = new SampleRequest("payload", 42);

        var bytes = JobsHelper.CreateJobRequest(request);

        JobsHelper.ReadJobRequest<SampleRequest>(bytes).Should().Be(request);
        JobsHelper.ReadJobRequestAsString(bytes).Should().Be("{\"Name\":\"payload\",\"Value\":42}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_return_null_for_json_null(bool compressed)
    {
        JobsHelper.UseGZipCompression = compressed;
        var bytes = JobsHelper.CreateJobRequest(Encoding.UTF8.GetBytes("null"));

        JobsHelper.ReadJobRequest<SampleRequest>(bytes).Should().BeNull();
    }

    [Fact]
    public void should_reject_empty_or_malformed_plain_json()
    {
        JobsHelper.UseGZipCompression = false;

        var readEmpty = () => JobsHelper.ReadJobRequest<SampleRequest>([]);
        var readMalformed = () => JobsHelper.ReadJobRequest<SampleRequest>("{"u8.ToArray());

        readEmpty.Should().Throw<JsonException>();
        readMalformed.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_reject_missing_sentinel_or_truncated_compressed_json()
    {
        JobsHelper.UseGZipCompression = true;

        var readMissing = () => JobsHelper.ReadJobRequest<SampleRequest>([0x1f, 0x8b, 0x08]);
        var readTruncated = () =>
            JobsHelper.ReadJobRequest<SampleRequest>([0x1f, 0x8b, 0x08, 0x00, 0x1f, 0x8b, 0x08, 0x00]);

        readMissing.Should().Throw<InvalidOperationException>();
        readTruncated.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_only_recognize_the_trailing_signature()
    {
        JobsHelper.UseGZipCompression = true;
        byte[] bytes = [0x1f, 0x8b, 0x08, 0x00, 0x01];

        var read = () => JobsHelper.ReadJobRequest<SampleRequest>(bytes);

        read.Should().Throw<InvalidOperationException>();
    }

    private sealed record SampleRequest(string Name, int Value);
}
