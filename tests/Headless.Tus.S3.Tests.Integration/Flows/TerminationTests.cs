// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.Flows;

/// <summary>
/// A tus DELETE through the real middleware removes every object the upload owns, including objects its state never
/// recorded: a tail segment and a multipart upload a crashed writer left behind.
/// </summary>
public abstract class TerminationTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = "termination/",
        PartSize = 5 * _MiB,
    };

    private TusS3RawObjects _raw = null!;
    private TusS3TestHost _host = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _raw = new TusS3RawObjects(_client, _options);
        _host = await TusS3TestHost.StartAsync(_client, _options, LoggerFactory, timeProvider: null, AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _host.Dispose();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_remove_every_object_of_an_incomplete_upload()
    {
        // given - a part, a tail segment, a segment the state never recorded, and a multipart upload it never recorded
        var location = await _host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);

        using (var patch = await _host.PatchAsync(location, Faker.Random.Bytes(6 * _MiB), 0, AbortToken))
        {
            patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var state = (await _raw.ReadStateAsync(fileId, AbortToken))!;
        state.Parts.Should().ContainSingle();
        state.Segments.Should().ContainSingle();
        await _PutAsync(_raw.Keys.Segments(fileId) + "orphan", Faker.Random.Bytes(100));
        await _client.S3.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _options.Bucket, Key = _raw.Keys.FinishedObject(fileId) },
            AbortToken
        );

        // when
        using var deleted = await _DeleteAsync(location);

        // then
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(fileId))).Should().BeEmpty();
        (await _ListMultipartUploadKeysAsync(_raw.Keys.FinishedObject(fileId)))
            .Should()
            .NotContain(_raw.Keys.FinishedObject(fileId));
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().BeNull();

        using var head = await _host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_remove_the_finished_object_and_record_of_a_completed_upload()
    {
        // given
        var content = Faker.Random.Bytes(400);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);

        using (var patch = await _host.PatchAsync(location, content, 0, AbortToken))
        {
            patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);

        // when
        using var deleted = await _DeleteAsync(location);

        // then
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken)).Should().BeNull();

        using var head = await _host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> _DeleteAsync(string location)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        return await _host.Client.SendAsync(request, AbortToken);
    }

    private async Task _PutAsync(string key, byte[] content)
    {
        await using var body = new MemoryStream(content);
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
        };
        TusS3RequestPresets.Apply(request, _client.Provider);

        await _client.S3.PutObjectAsync(request, AbortToken);
    }

    private async Task<List<string>> _ListKeysAsync(string prefix)
    {
        var response = await _client.S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix },
            AbortToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key)];
    }

    private async Task<List<string>> _ListMultipartUploadKeysAsync(string key)
    {
        var response = await _client.S3.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { BucketName = _options.Bucket, Prefix = key },
            AbortToken
        );

        return [.. (response.MultipartUploads ?? []).Select(u => u.Key)];
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTerminationTests(TusS3MinioFixture fixture) : TerminationTestsBase(fixture)
{
    [Fact]
    public override Task should_remove_every_object_of_an_incomplete_upload() =>
        base.should_remove_every_object_of_an_incomplete_upload();

    [Fact]
    public override Task should_remove_the_finished_object_and_record_of_a_completed_upload() =>
        base.should_remove_the_finished_object_and_record_of_a_completed_upload();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTerminationTests(TusS3LocalStackFixture fixture) : TerminationTestsBase(fixture)
{
    [Fact]
    public override Task should_remove_every_object_of_an_incomplete_upload() =>
        base.should_remove_every_object_of_an_incomplete_upload();

    [Fact]
    public override Task should_remove_the_finished_object_and_record_of_a_completed_upload() =>
        base.should_remove_the_finished_object_and_record_of_a_completed_upload();
}
