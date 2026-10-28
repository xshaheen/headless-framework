// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;

namespace Tests.Flows;

/// <summary>
/// How a final upload is built from partials on S3: which parts the provider copies and which the store streams, that
/// every non-final part has the part size R2 requires, and that a failed build leaves neither a visible upload nor a
/// multipart upload behind.
/// </summary>
public abstract class ConcatenationTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"concat-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private TusS3RawObjects _raw = null!;
    private TusS3TestHost _host = null!;

    private TusS3Store Store => _host.Store;

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
        _client.Gate.FailWhen(null);
        _host.Dispose();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_build_equal_parts_from_partials_whose_boundaries_fall_inside_parts()
    {
        // given - 3 MiB, 13 MiB, and 0.5 MiB partials; the final's 5 MiB parts straddle the first and last boundary
        var (ids, contents) = await _UploadPartialsAsync(3 * _MiB, 13 * _MiB, _MiB / 2);
        _client.Gate.Reset();

        // when
        var finalId = await Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then - the contents in order, every part but the last exactly 5 MiB
        (await _raw.ReadFinishedObjectAsync(finalId, AbortToken))
            .Should()
            .Equal(contents.SelectMany(content => content));
        (await _raw.GetFinishedObjectPartSizesAsync(finalId, AbortToken))
            .Should()
            .Equal(5 * _MiB, 5 * _MiB, 5 * _MiB, _MiB + (_MiB / 2));
        // the two parts inside the 13 MiB partial are copied; the two that span partials are streamed
        _client.Gate.Count(S3Operation.UploadPartCopy).Should().Be(2);
        _client.Gate.Count(S3Operation.UploadPart).Should().Be(2);
        _client.Gate.Count(S3Operation.CompleteMultipartUpload).Should().Be(1);
    }

    public virtual async Task should_copy_every_part_when_partial_boundaries_align_with_the_part_size()
    {
        // given
        var (ids, contents) = await _UploadPartialsAsync(5 * _MiB, 10 * _MiB, 2 * _MiB);
        _client.Gate.Reset();

        // when
        var finalId = await Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then - no byte passes through the store
        _client.Gate.Count(S3Operation.UploadPartCopy).Should().Be(4, "counts: {0}", _client.Gate.Counts);
        _client.Gate.Count(S3Operation.UploadPart).Should().Be(0);
        (await _raw.GetFinishedObjectPartSizesAsync(finalId, AbortToken))
            .Should()
            .Equal(5 * _MiB, 5 * _MiB, 5 * _MiB, 2 * _MiB);
        (await _raw.ReadFinishedObjectAsync(finalId, AbortToken)).Should().Equal(contents.SelectMany(c => c));
    }

    public virtual async Task should_write_a_final_upload_below_one_part_as_one_object()
    {
        // given
        var (ids, contents) = await _UploadPartialsAsync(300_000, 0, 1_200_000);
        _client.Gate.Reset();

        // when
        var finalId = await Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then
        _client.Gate.Count(S3Operation.CreateMultipartUpload).Should().Be(0);
        (await _raw.GetFinishedObjectPartSizesAsync(finalId, AbortToken)).Should().Equal(1_500_000);
        (await _raw.ReadFinishedObjectAsync(finalId, AbortToken)).Should().Equal(contents.SelectMany(c => c));
        (await _raw.ReadCompletedRecordAsync(finalId, AbortToken))!.UploadLength.Should().Be(1_500_000);
    }

    public virtual async Task should_write_an_empty_object_when_every_partial_is_empty()
    {
        // given
        var (ids, _) = await _UploadPartialsAsync(0, 0);

        // when
        var finalId = await Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then
        (await _raw.ReadFinishedObjectAsync(finalId, AbortToken))
            .Should()
            .BeEmpty();
        (await Store.GetUploadLengthAsync(finalId, AbortToken)).Should().Be(0);
        (await Store.GetUploadOffsetAsync(finalId, AbortToken)).Should().Be(0);
    }

    public virtual async Task should_report_a_complete_final_upload_with_its_partials_on_head()
    {
        // given - partials uploaded, then a final created through the tus endpoint
        var (ids, _) = await _UploadPartialsAsync(2 * _MiB, 4 * _MiB);
        var urls = string.Join(' ', ids.Select(id => $"{TusS3TestHost.Endpoint}/{id}"));

        using var create = new HttpRequestMessage(HttpMethod.Post, TusS3TestHost.Endpoint);
        create.Headers.Add("Tus-Resumable", "1.0.0");
        create.Headers.Add("Upload-Concat", $"final;{urls}");
        using var created = await _host.Client.SendAsync(create, AbortToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        // when
        using var head = await _host.HeadAsync(created.Headers.Location!.ToString(), AbortToken);

        // then
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be($"{6 * _MiB}");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be($"{6 * _MiB}");
        head.Headers.GetValues("Upload-Concat").Should().ContainSingle().Which.Should().StartWith("final;");

        var finalId = TusS3TestHost.FileIdOf(created.Headers.Location!.ToString());
        var concat = await Store.GetUploadConcatAsync(finalId, AbortToken);
        concat.Should().BeOfType<FileConcatFinal>().Which.Files.Should().Equal(ids);
        (await _raw.ReadStateAsync(finalId, AbortToken)).Should().BeNull("a final upload never has a working set");
    }

    public virtual async Task should_start_no_multipart_upload_when_a_partial_is_incomplete()
    {
        // given - one complete partial and one holding half its bytes
        var (ids, _) = await _UploadPartialsAsync(6 * _MiB);
        var incomplete = await Store.CreatePartialFileAsync(6 * _MiB, metadata: null, AbortToken);
        await using (var body = new MemoryStream(Faker.Random.Bytes(3 * _MiB)))
        {
            await Store.AppendDataAsync(incomplete, body, AbortToken);
        }

        _client.Gate.Reset();

        // when
        var act = () => Store.CreateFinalFileAsync([ids[0], incomplete], metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<TusStoreException>().WithMessage($"Partial file {incomplete} is incomplete*");
        _client.Gate.Count(S3Operation.CreateMultipartUpload).Should().Be(0);
        _client.Gate.Count(S3Operation.PutObject).Should().Be(0);
    }

    public virtual async Task should_abort_the_build_when_a_partial_object_disappears_after_validation()
    {
        // given - aligned partials, so the second is read by a part copy; its finished object is then removed
        var (ids, _) = await _UploadPartialsAsync(5 * _MiB, 5 * _MiB);
        await _client.S3.DeleteObjectAsync(_options.Bucket, _raw.Keys.FinishedObject(ids[1]), AbortToken);
        var recordsBefore = await _ListCompletedRecordsAsync();
        _client.Gate.Reset();

        // when
        var act = () => Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<TusStoreException>().WithMessage($"Partial file {ids[1]} does not exist");
        await _AssertNoFinalUploadLeftAsync(recordsBefore);
    }

    public virtual async Task should_abort_the_multipart_upload_when_a_part_fails_mid_build()
    {
        // given - partials whose final has copied parts, and every part copy failing
        var (ids, _) = await _UploadPartialsAsync(3 * _MiB, 13 * _MiB, _MiB / 2);
        var recordsBefore = await _ListCompletedRecordsAsync();
        _client.Gate.Reset();
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.UploadPartCopy, StringComparison.Ordinal)
        );

        // when
        var act = () => Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<AmazonS3Exception>();
        _client.Gate.FailWhen(null);
        await _AssertNoFinalUploadLeftAsync(recordsBefore);
    }

    public virtual async Task should_delete_the_finished_object_when_its_record_write_fails(bool multipart)
    {
        // given - partials whose final is built by a multipart upload or by one write, and the final's completed
        // record write failing; the partials' own records are already written
        var (ids, _) = multipart
            ? await _UploadPartialsAsync(3 * _MiB, 6 * _MiB)
            : await _UploadPartialsAsync(300, 700);
        var recordsBefore = await _ListCompletedRecordsAsync();
        var recordFolder = "/" + _options.NormalizedPrefix + TusS3Keys.CompletedFolder;
        string? finalId = null;
        _client.Gate.FailWhen(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var folder = path.IndexOf(recordFolder, StringComparison.Ordinal);

            if (folder < 0 || !string.Equals(S3Operation.Of(request), S3Operation.PutObject, StringComparison.Ordinal))
            {
                return false;
            }

            finalId = path[(folder + recordFolder.Length)..];

            return true;
        });

        // when
        var act = () => Store.CreateFinalFileAsync(ids, metadata: null, AbortToken);

        // then - the build fails, and neither its finished object nor a record is left behind
        await act.Should().ThrowAsync<AmazonS3Exception>();
        _client.Gate.FailWhen(null);
        finalId.Should().NotBeNull("the build reached its record write");
        (await _raw.ReadFinishedObjectAsync(finalId!, AbortToken)).Should().BeNull();
        (await _ListCompletedRecordsAsync()).Should().Equal(recordsBefore);
    }

    private async Task _AssertNoFinalUploadLeftAsync(IReadOnlyList<string> recordsBefore)
    {
        _client.Gate.Count(S3Operation.CreateMultipartUpload).Should().Be(1);
        _client.Gate.Count(S3Operation.AbortMultipartUpload).Should().Be(1);
        _client.Gate.Count(S3Operation.CompleteMultipartUpload).Should().Be(0);
        (await _ListCompletedRecordsAsync()).Should().Equal(recordsBefore);

        var uploads = await _client.S3.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { BucketName = _options.Bucket, Prefix = _options.Prefix },
            AbortToken
        );
        (uploads.MultipartUploads ?? []).Should().BeEmpty();
    }

    private async Task<IReadOnlyList<string>> _ListCompletedRecordsAsync()
    {
        var response = await _client.S3.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = _options.Prefix + TusS3Keys.CompletedFolder,
            },
            AbortToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key).Order(StringComparer.Ordinal)];
    }

    private async Task<(string[] Ids, byte[][] Contents)> _UploadPartialsAsync(params int[] lengths)
    {
        var ids = new string[lengths.Length];
        var contents = new byte[lengths.Length][];

        for (var i = 0; i < lengths.Length; i++)
        {
            contents[i] = Faker.Random.Bytes(lengths[i]);
            ids[i] = await Store.CreatePartialFileAsync(lengths[i], metadata: null, AbortToken);

            if (lengths[i] > 0)
            {
                await using var body = new MemoryStream(contents[i], writable: false);
                (await Store.AppendDataAsync(ids[i], body, AbortToken)).Should().Be(lengths[i]);
            }
        }

        return (ids, contents);
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioConcatenationTests(TusS3MinioFixture fixture) : ConcatenationTestsBase(fixture)
{
    [Fact]
    public override Task should_build_equal_parts_from_partials_whose_boundaries_fall_inside_parts() =>
        base.should_build_equal_parts_from_partials_whose_boundaries_fall_inside_parts();

    [Fact]
    public override Task should_copy_every_part_when_partial_boundaries_align_with_the_part_size() =>
        base.should_copy_every_part_when_partial_boundaries_align_with_the_part_size();

    [Fact]
    public override Task should_write_a_final_upload_below_one_part_as_one_object() =>
        base.should_write_a_final_upload_below_one_part_as_one_object();

    [Fact]
    public override Task should_write_an_empty_object_when_every_partial_is_empty() =>
        base.should_write_an_empty_object_when_every_partial_is_empty();

    [Fact]
    public override Task should_report_a_complete_final_upload_with_its_partials_on_head() =>
        base.should_report_a_complete_final_upload_with_its_partials_on_head();

    [Fact]
    public override Task should_start_no_multipart_upload_when_a_partial_is_incomplete() =>
        base.should_start_no_multipart_upload_when_a_partial_is_incomplete();

    [Fact]
    public override Task should_abort_the_build_when_a_partial_object_disappears_after_validation() =>
        base.should_abort_the_build_when_a_partial_object_disappears_after_validation();

    [Fact]
    public override Task should_abort_the_multipart_upload_when_a_part_fails_mid_build() =>
        base.should_abort_the_multipart_upload_when_a_part_fails_mid_build();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task should_delete_the_finished_object_when_its_record_write_fails(bool multipart) =>
        base.should_delete_the_finished_object_when_its_record_write_fails(multipart);
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackConcatenationTests(TusS3LocalStackFixture fixture) : ConcatenationTestsBase(fixture)
{
    [Fact]
    public override Task should_build_equal_parts_from_partials_whose_boundaries_fall_inside_parts() =>
        base.should_build_equal_parts_from_partials_whose_boundaries_fall_inside_parts();

    [Fact]
    public override Task should_copy_every_part_when_partial_boundaries_align_with_the_part_size() =>
        base.should_copy_every_part_when_partial_boundaries_align_with_the_part_size();

    [Fact]
    public override Task should_write_a_final_upload_below_one_part_as_one_object() =>
        base.should_write_a_final_upload_below_one_part_as_one_object();

    [Fact]
    public override Task should_write_an_empty_object_when_every_partial_is_empty() =>
        base.should_write_an_empty_object_when_every_partial_is_empty();

    [Fact]
    public override Task should_report_a_complete_final_upload_with_its_partials_on_head() =>
        base.should_report_a_complete_final_upload_with_its_partials_on_head();

    [Fact]
    public override Task should_start_no_multipart_upload_when_a_partial_is_incomplete() =>
        base.should_start_no_multipart_upload_when_a_partial_is_incomplete();

    [Fact]
    public override Task should_abort_the_build_when_a_partial_object_disappears_after_validation() =>
        base.should_abort_the_build_when_a_partial_object_disappears_after_validation();

    [Fact]
    public override Task should_abort_the_multipart_upload_when_a_part_fails_mid_build() =>
        base.should_abort_the_multipart_upload_when_a_part_fails_mid_build();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task should_delete_the_finished_object_when_its_record_write_fails(bool multipart) =>
        base.should_delete_the_finished_object_when_its_record_write_fails(multipart);
}
