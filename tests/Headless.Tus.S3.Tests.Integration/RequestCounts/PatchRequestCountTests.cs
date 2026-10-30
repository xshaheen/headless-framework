// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.RequestCounts;

/// <summary>
/// Pins how many storage requests a PATCH costs through the real tusdotnet pipeline. The host keeps no sliding
/// expiration, whose refresh is still a state write of its own, so the counts are the append's alone.
/// </summary>
public abstract class PatchRequestCountTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = "patch-counts/",
        PartSize = 8 * _MiB,
    };

    private TusS3TestHost _host = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken,
            slidingExpiration: false
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _host.Dispose();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_store_a_tail_only_patch_in_three_requests()
    {
        // given
        var location = await _host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        _client.Gate.Reset();

        // when
        using var patch = await _host.PatchAsync(location, Faker.Random.Bytes(_MiB), 0, AbortToken);

        // then - read the state, write the segment, commit
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _client.Gate.TotalRequests.Should().Be(3, "counts: {0}", _Describe());
        _client.Gate.Count(S3Operation.GetObject).Should().Be(1);
        _client.Gate.Count(S3Operation.PutObject).Should().Be(2);
    }

    public virtual async Task should_write_one_part_from_a_single_segment_in_the_planned_requests()
    {
        // given - 1 MiB already in a tail segment
        var location = await _host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        using (var first = await _host.PatchAsync(location, Faker.Random.Bytes(_MiB), 0, AbortToken))
        {
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        _client.Gate.Reset();

        // when - 8 MiB more: one part and a 1 MiB remainder
        using var patch = await _host.PatchAsync(location, Faker.Random.Bytes(8 * _MiB), _MiB, AbortToken);

        // then - N + 4 + ceil(N / 64) for N = 1, one segment read, and the multipart upload's creation: read the
        // state, create the multipart upload, reserve part numbers, read the segment, upload the part, write the
        // remainder, commit, delete the consumed segment
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be((9 * _MiB).ToString());
        _client.Gate.TotalRequests.Should().Be(8, "counts: {0}", _Describe());
        _client.Gate.Count(S3Operation.GetObject).Should().Be(2);
        _client.Gate.Count(S3Operation.CreateMultipartUpload).Should().Be(1);
        _client.Gate.Count(S3Operation.PutObject).Should().Be(3);
        _client.Gate.Count(S3Operation.UploadPart).Should().Be(1);
        _client.Gate.Count(S3Operation.DeleteObjects).Should().Be(1);

        var state = await new TusS3RawObjects(_client, _options).ReadStateAsync(
            TusS3TestHost.FileIdOf(location),
            AbortToken
        );
        state!.Parts.Should().ContainSingle().Which.Size.Should().Be(8 * _MiB);
        state.Segments.Should().ContainSingle().Which.Length.Should().Be(_MiB);
    }

    private string _Describe()
    {
        return string.Join(", ", _client.Gate.Counts.Select(pair => $"{pair.Key}={pair.Value}"));
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioPatchRequestCountTests(TusS3MinioFixture fixture) : PatchRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_store_a_tail_only_patch_in_three_requests() =>
        base.should_store_a_tail_only_patch_in_three_requests();

    [Fact]
    public override Task should_write_one_part_from_a_single_segment_in_the_planned_requests() =>
        base.should_write_one_part_from_a_single_segment_in_the_planned_requests();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackPatchRequestCountTests(TusS3LocalStackFixture fixture)
    : PatchRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_store_a_tail_only_patch_in_three_requests() =>
        base.should_store_a_tail_only_patch_in_three_requests();

    [Fact]
    public override Task should_write_one_part_from_a_single_segment_in_the_planned_requests() =>
        base.should_write_one_part_from_a_single_segment_in_the_planned_requests();
}
