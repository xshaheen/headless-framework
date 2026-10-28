// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.Time.Testing;
using Tests.TestSetup;

namespace Tests.Flows;

/// <summary>
/// A state object the store cannot parse, whether corrupted or written with a status a newer build added, must not stop
/// the expiry sweep: the sweep skips it and still removes every other abandoned upload under the prefix.
/// </summary>
public abstract class CorruptStateSweepTestsBase(IS3TestBackend backend) : TestBase
{
    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"corrupt-{Guid.NewGuid():N}/",
    };

    private TusS3RawObjects _raw = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _raw = new TusS3RawObjects(_client, _options);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_remove_an_expired_upload_beside_one_whose_state_cannot_be_parsed(string content)
    {
        // given - two expired uploads, one of whose state objects no longer parses
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory, timeProvider: clock);
        var unreadable = await store.CreateFileAsync(1000, metadata: null, AbortToken);
        var expired = await store.CreateFileAsync(1000, metadata: null, AbortToken);
        await store.SetExpirationAsync(unreadable, clock.GetUtcNow().AddMinutes(10), AbortToken);
        await store.SetExpirationAsync(expired, clock.GetUtcNow().AddMinutes(10), AbortToken);
        await _PutAsync(_raw.Keys.Info(unreadable), Encoding.UTF8.GetBytes(content));
        clock.Advance(TimeSpan.FromMinutes(30));

        // when
        var removed = await store.RemoveExpiredFilesAsync(AbortToken);

        // then - the readable upload is removed and the unreadable one is left as it was
        removed.Should().Be(1);
        (await _raw.ReadStateAsync(expired, AbortToken)).Should().BeNull();
        (await _raw.GetSizeAsync(_raw.Keys.Info(unreadable), AbortToken))
            .Should()
            .Be(Encoding.UTF8.GetByteCount(content));
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
}

[Collection<TusS3MinioFixture>]
public sealed class MinioCorruptStateSweepTests(TusS3MinioFixture fixture) : CorruptStateSweepTestsBase(fixture)
{
    [Theory]
    [InlineData("""{"schemaVersion":1,"partSize":""")]
    [InlineData("""{"schemaVersion":1,"partSize":5242880,"status":"Paused"}""")]
    public override Task should_remove_an_expired_upload_beside_one_whose_state_cannot_be_parsed(string content) =>
        base.should_remove_an_expired_upload_beside_one_whose_state_cannot_be_parsed(content);
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackCorruptStateSweepTests(TusS3LocalStackFixture fixture)
    : CorruptStateSweepTestsBase(fixture)
{
    [Theory]
    [InlineData("""{"schemaVersion":1,"partSize":""")]
    [InlineData("""{"schemaVersion":1,"partSize":5242880,"status":"Paused"}""")]
    public override Task should_remove_an_expired_upload_beside_one_whose_state_cannot_be_parsed(string content) =>
        base.should_remove_an_expired_upload_beside_one_whose_state_cannot_be_parsed(content);
}
