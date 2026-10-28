// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.RequestCounts;

/// <summary>
/// Pins how many storage requests HEAD and creation cost through the real tusdotnet pipeline, which calls several
/// store methods per request: the store must answer them all from one read.
/// </summary>
public abstract class HeadRequestCountTestsBase(IS3TestBackend backend) : TestBase
{
    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new() { Bucket = TusS3StoreFactory.Bucket, Prefix = "head-counts/" };

    private TusS3TestHost _host = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _host = await TusS3TestHost.StartAsync(_client, _options, LoggerFactory, timeProvider: null, AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _host.Dispose();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_answer_head_on_an_incomplete_upload_with_one_request()
    {
        // given
        const string metadata = "filename dGVzdC5iaW4=";
        var location = await _host.CreateUploadAsync(10 * 1024 * 1024, metadata, AbortToken);
        _client.Gate.Reset();

        // when
        using var head = await _host.HeadAsync(location, AbortToken);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("10485760");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
        head.Headers.GetValues("Upload-Metadata").Should().ContainSingle().Which.Should().Be(metadata);
        _client.Gate.TotalRequests.Should().Be(1, "counts: {0}", _Describe());
        _client.Gate.Count(S3Operation.GetObject).Should().Be(1);
    }

    public virtual async Task should_answer_head_on_a_completed_upload_with_two_requests()
    {
        // given - a zero-length upload completes at creation
        var location = await _host.CreateUploadAsync(0, metadata: null, AbortToken);
        _client.Gate.Reset();

        // when
        using var head = await _host.HeadAsync(location, AbortToken);

        // then - the state object is gone, so HEAD falls back to the completed record
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("0");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
        _client.Gate.TotalRequests.Should().Be(2, "counts: {0}", _Describe());
        _client.Gate.Count(S3Operation.GetObject).Should().Be(2);
    }

    public virtual async Task should_create_an_upload_with_expiration_in_two_requests()
    {
        // given
        _client.Gate.Reset();

        // when - creation writes the state, then tusdotnet sets the sliding expiration on it
        var location = await _host.CreateUploadAsync(100, metadata: null, AbortToken);

        // then - the expiration write reuses the ETag creation returned instead of reading the state again
        _client.Gate.TotalRequests.Should().Be(2, "counts: {0}", _Describe());
        _client.Gate.Count(S3Operation.PutObject).Should().Be(2);
        (await _host.Store.GetExpirationAsync(TusS3TestHost.FileIdOf(location), AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_read_the_state_again_in_a_new_request()
    {
        // given
        var location = await _host.CreateUploadAsync(100, metadata: null, AbortToken);
        using var first = await _host.HeadAsync(location, AbortToken);
        first.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");

        // when - another writer changes the upload between two requests
        await new TusS3RawObjects(_client, _options).RewriteStateAsync(
            TusS3TestHost.FileIdOf(location),
            state => state.Offset = 42,
            AbortToken
        );

        using var head = await _host.HeadAsync(location, AbortToken);

        // then - the memo lives for one request only
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("42");
    }

    private string _Describe()
    {
        return string.Join(", ", _client.Gate.Counts.Select(pair => $"{pair.Key}={pair.Value}"));
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioHeadRequestCountTests(TusS3MinioFixture fixture) : HeadRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_answer_head_on_an_incomplete_upload_with_one_request() =>
        base.should_answer_head_on_an_incomplete_upload_with_one_request();

    [Fact]
    public override Task should_answer_head_on_a_completed_upload_with_two_requests() =>
        base.should_answer_head_on_a_completed_upload_with_two_requests();

    [Fact]
    public override Task should_create_an_upload_with_expiration_in_two_requests() =>
        base.should_create_an_upload_with_expiration_in_two_requests();

    [Fact]
    public override Task should_read_the_state_again_in_a_new_request() =>
        base.should_read_the_state_again_in_a_new_request();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackHeadRequestCountTests(TusS3LocalStackFixture fixture) : HeadRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_answer_head_on_an_incomplete_upload_with_one_request() =>
        base.should_answer_head_on_an_incomplete_upload_with_one_request();

    [Fact]
    public override Task should_answer_head_on_a_completed_upload_with_two_requests() =>
        base.should_answer_head_on_a_completed_upload_with_two_requests();

    [Fact]
    public override Task should_create_an_upload_with_expiration_in_two_requests() =>
        base.should_create_an_upload_with_expiration_in_two_requests();

    [Fact]
    public override Task should_read_the_state_again_in_a_new_request() =>
        base.should_read_the_state_again_in_a_new_request();
}
