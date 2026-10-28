// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Provisioning;

public abstract class TusS3BucketProvisionerTestsBase(IS3TestBackend backend) : TestBase
{
    private const string _Prefix = "uploads/";

    private readonly S3TestClient _client = backend.CreateClient();

    // A bucket per test: purge and delete assertions must not see another test's objects.
    private readonly TusS3StoreOptions _options = new() { Bucket = $"tusprov-{Guid.NewGuid():N}", Prefix = _Prefix };

    private IAmazonS3 _S3 => _client.S3;

    private TusS3BucketProvisioner _CreateProvisioner() => new(_S3, _options, LoggerFactory);

    public virtual async Task should_create_missing_bucket_when_provisioning()
    {
        // given
        var provisioner = _CreateProvisioner();

        // when - a second run finds the bucket it already owns
        await provisioner.ProvisionAsync(AbortToken);
        await provisioner.ProvisionAsync(AbortToken);

        // then
        (await _BucketExistsAsync())
            .Should()
            .BeTrue();
    }

    public virtual async Task should_fail_verification_when_bucket_is_missing()
    {
        // given
        var provisioner = _CreateProvisioner();

        // when
        var act = () => provisioner.VerifyAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage($"*'{_options.Bucket}'*resources provision -k S3Bucket*");
    }

    public virtual async Task should_verify_existing_bucket_with_one_request()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        _client.Gate.Reset();

        // when
        await provisioner.VerifyAsync(AbortToken);

        // then
        _client.Gate.TotalRequests.Should().Be(1, "verification is the start-up check and must stay one cheap read");
    }

    public virtual async Task should_report_bucket_state_in_status()
    {
        // given
        _options.Prefix = "uploads";
        var provisioner = _CreateProvisioner();

        // when
        var before = await provisioner.GetStatusAsync(AbortToken);
        await provisioner.ProvisionAsync(AbortToken);
        var after = await provisioner.GetStatusAsync(AbortToken);

        // then
        before.Should().Contain(new KeyValuePair<string, string>("bucket", "missing"));
        after.Should().Contain(new KeyValuePair<string, string>("bucket", "exists"));
        after.Should().Contain(new KeyValuePair<string, string>("prefix", "uploads/"));
    }

    public virtual async Task should_purge_store_objects_and_keep_foreign_keys()
    {
        // given - one in-progress upload, one completed upload, and objects the store does not own
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _PutAsync(_Prefix + ".tus/in-progress/info");
        await _PutAsync(_Prefix + ".tus/in-progress/seg/0-abc");
        await _PutAsync(_Prefix + ".tus-completed/done");
        await _PutAsync(_Prefix + "done");
        await _PutAsync(_Prefix + "foreign");
        await _PutAsync("other/kept");

        // when
        await provisioner.PurgeAsync(AbortToken);

        // then
        (await _KeysAsync())
            .Should()
            .BeEquivalentTo(_Prefix + "foreign", "other/kept");
    }

    public virtual async Task should_purge_nothing_when_bucket_is_missing()
    {
        // given
        var provisioner = _CreateProvisioner();

        // when
        var act = () => provisioner.PurgeAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
        (await _BucketExistsAsync()).Should().BeFalse();
    }

    public virtual async Task should_delete_bucket_when_it_holds_only_store_objects()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _PutAsync(_Prefix + ".tus/in-progress/info");
        await _PutAsync(_Prefix + ".tus-completed/done");
        await _PutAsync(_Prefix + "done");

        // when - a second run finds nothing to delete
        await provisioner.DeleteAsync(AbortToken);
        await provisioner.DeleteAsync(AbortToken);

        // then
        (await _BucketExistsAsync())
            .Should()
            .BeFalse();
    }

    public virtual async Task should_keep_bucket_holding_foreign_keys_when_deleting()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _PutAsync(_Prefix + ".tus/in-progress/info");
        await _PutAsync("other/kept");

        // when
        await provisioner.DeleteAsync(AbortToken);

        // then
        (await _BucketExistsAsync())
            .Should()
            .BeTrue();
        (await _KeysAsync()).Should().Equal("other/kept");
    }

    public virtual async Task should_create_bucket_through_registered_provisioner()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_S3);
        services.AddTusS3Store(options =>
        {
            options.Bucket = _options.Bucket;
            options.Prefix = _Prefix;
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        (await _BucketExistsAsync())
            .Should()
            .BeTrue();
    }

    public virtual Task should_make_no_storage_request_when_constructing_the_store()
    {
        // when
        _ = new TusS3Store(_S3, _options, LoggerFactory);

        // then
        _client.Gate.TotalRequests.Should().Be(0);

        return Task.CompletedTask;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        // Best effort: a test that deleted its bucket, or never created one, leaves nothing to remove.
        await _CreateProvisioner().DeleteAsync(CancellationToken.None);
        await _DeleteRemainingAsync();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    private async Task _DeleteRemainingAsync()
    {
        if (!await _BucketExistsAsync())
        {
            return;
        }

        foreach (var key in await _KeysAsync())
        {
            await _S3.DeleteObjectAsync(_options.Bucket, key, CancellationToken.None);
        }

        await _S3.DeleteBucketAsync(_options.Bucket, CancellationToken.None);
    }

    private async Task _PutAsync(string key)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            ContentBody = "data",
        };
        TusS3RequestPresets.Apply(request, _client.Provider);

        await _S3.PutObjectAsync(request, AbortToken);
    }

    private async Task<bool> _BucketExistsAsync()
    {
        try
        {
            await _S3.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = _options.Bucket, MaxKeys = 1 },
                CancellationToken.None
            );

            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<List<string>> _KeysAsync()
    {
        var response = await _S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket },
            CancellationToken.None
        );

        return (response.S3Objects ?? []).ConvertAll(o => o.Key);
    }
}
