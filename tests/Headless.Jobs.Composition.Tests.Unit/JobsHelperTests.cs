// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
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
    public void should_round_trip_typed_and_textual_payloads(bool compressed)
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

    [Fact]
    public void should_serialize_with_configured_options_that_declare_no_type_info_resolver()
    {
        // ConfigureRequestJsonOptions starts from new JsonSerializerOptions(), which has no resolver; metadata lookups
        // through GetTypeInfo throw on such options unless Jobs fills the resolver in.
        var options = new JobsRequestSerializationOptions
        {
            SerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
        };
        var request = new SampleRequest("payload", 42);

        var bytes = JobsHelper.CreateJobRequest(request, options);

        JobsHelper.ReadJobRequestAsString(bytes, options).Should().Be("{\"name\":\"payload\",\"value\":42}");
        JobsHelper.ReadJobRequest<SampleRequest>(bytes, options).Should().Be(request);
        options.SerializerOptions.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void should_serialize_through_a_source_generated_context_alone()
    {
        var sourceGenerated = new JobsRequestSerializationOptions
        {
            SerializerOptions = new JsonSerializerOptions { TypeInfoResolver = JobsHelperTestsJsonContext.Default },
        };
        var request = new SampleRequest("payload", 42);

        var bytes = JobsHelper.CreateJobRequest(request, sourceGenerated);
#pragma warning disable CA2263 // The non-generic overload is the one under test: it resolves metadata from a runtime Type.
        var boxedBytes = JobsHelper.CreateJobRequest(request, typeof(SampleRequest), sourceGenerated);
#pragma warning restore CA2263

        bytes.Should().Equal(JobsHelper.CreateJobRequest(request, _Options(compressed: false)));
        boxedBytes.Should().Equal(bytes);
        JobsHelper.ReadJobRequest<SampleRequest>(bytes, sourceGenerated).Should().Be(request);
    }

    internal sealed record SampleRequest(string Name, int Value);
}

[JsonSerializable(typeof(JobsHelperTests.SampleRequest))]
internal sealed partial class JobsHelperTestsJsonContext : JsonSerializerContext;
