// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;
using tusdotnet.Models.Concatenation;

namespace Tests;

/// <summary>
/// Concatenation behavior every tus store must share: final uploads are validated against their partials, report
/// their concat type, and hold the partials' bytes in order.
/// </summary>
public abstract class TusConcatenationConformanceTestsBase<TStore>(ITusConformanceBackend<TStore> backend)
    : TusConformanceTestsBase<TStore>(backend)
    where TStore : class,
        ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    public virtual async Task should_throw_when_create_final_file_with_empty_partials()
    {
        // given
        string[] emptyPartials = [];

        // when
        var act = async () => await Store.CreateFinalFileAsync(emptyPartials, metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_throw_when_create_final_file_with_nonexistent_partial()
    {
        // given
        var nonExistentPartialId = Guid.NewGuid().ToString("n");

        // when
        var act = async () => await Store.CreateFinalFileAsync([nonExistentPartialId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"Partial file {nonExistentPartialId} does not exist");
    }

    public virtual async Task should_throw_when_create_final_file_with_regular_file_as_partial()
    {
        // given - create a regular (non-partial) file
        var uploadLength = Faker.Random.Number(100, 500);
        var regularFileId = await Store.CreateFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var act = async () => await Store.CreateFinalFileAsync([regularFileId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"File {regularFileId} is not a partial file");
    }

    public virtual async Task should_throw_when_create_final_file_with_incomplete_partial()
    {
        // given - create partial file but don't upload all data
        const int uploadLength = 1000;
        var partialData = Faker.Random.Bytes(500); // Only half the data
        var partialId = await Store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(partialData))
        {
            await Store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when
        var act = async () => await Store.CreateFinalFileAsync([partialId], metadata: null, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<tusdotnet.Models.TusStoreException>()
            .WithMessage($"Partial file {partialId} is incomplete*");
    }

    public virtual async Task should_get_partial_file_info()
    {
        // given
        var uploadLength = Faker.Random.Number(100, 1_000);
        var partialId = await Store.CreatePartialFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var concat = await Store.GetUploadConcatAsync(partialId, AbortToken);

        // then
        concat.Should().NotBeNull();
        concat.Should().BeOfType<FileConcatPartial>();
    }

    public virtual async Task should_get_final_file_info()
    {
        // given - create complete partial files
        var data1 = Faker.Random.Bytes(300);
        var data2 = Faker.Random.Bytes(400);

        var partialId1 = await Store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await Store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await Store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await Store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var finalFileId = await Store.CreateFinalFileAsync([partialId1, partialId2], metadata: null, AbortToken);

        // when
        var concat = await Store.GetUploadConcatAsync(finalFileId, AbortToken);

        // then
        concat.Should().NotBeNull();
        concat.Should().BeOfType<FileConcatFinal>();

        var finalConcat = (FileConcatFinal)concat!;
        finalConcat.Files.Should().HaveCount(2);
        finalConcat.Files.Should().Contain(partialId1);
        finalConcat.Files.Should().Contain(partialId2);
    }

    public virtual async Task should_return_null_for_regular_file()
    {
        // given - create a regular (non-concatenation) file
        var uploadLength = Faker.Random.Number(100, 500);
        var regularFileId = await Store.CreateFileAsync(uploadLength, metadata: null, AbortToken);

        // when
        var concat = await Store.GetUploadConcatAsync(regularFileId, AbortToken);

        // then
        concat.Should().BeNull();
    }

    public virtual async Task should_return_null_for_nonexistent_file()
    {
        // given
        var nonExistentFileId = Guid.NewGuid().ToString("n");

        // when
        var concat = await Store.GetUploadConcatAsync(nonExistentFileId, AbortToken);

        // then
        concat.Should().BeNull();
    }

    public virtual async Task should_preserve_data_integrity_after_concatenation()
    {
        // given - create partial files with known content
        var data1 = Faker.Random.Bytes(1_000);
        var data2 = Faker.Random.Bytes(1_500);
        var data3 = Faker.Random.Bytes(800);

        var partialId1 = await Store.CreatePartialFileAsync(data1.Length, metadata: null, AbortToken);
        await using (var stream1 = new MemoryStream(data1))
        {
            await Store.AppendDataAsync(partialId1, stream1, AbortToken);
        }

        var partialId2 = await Store.CreatePartialFileAsync(data2.Length, metadata: null, AbortToken);
        await using (var stream2 = new MemoryStream(data2))
        {
            await Store.AppendDataAsync(partialId2, stream2, AbortToken);
        }

        var partialId3 = await Store.CreatePartialFileAsync(data3.Length, metadata: null, AbortToken);
        await using (var stream3 = new MemoryStream(data3))
        {
            await Store.AppendDataAsync(partialId3, stream3, AbortToken);
        }

        // when
        var finalFileId = await Store.CreateFinalFileAsync(
            [partialId1, partialId2, partialId3],
            metadata: null,
            AbortToken
        );

        // then - download and verify content
        var downloadedContent = await ReadContentAsync(finalFileId);
        var expectedContent = data1.Concat(data2).Concat(data3).ToArray();

        downloadedContent.Should().BeEquivalentTo(expectedContent);
    }

    public virtual async Task should_create_final_file_from_single_partial()
    {
        // given
        var data = Faker.Random.Bytes(500);
        var partialId = await Store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await Store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when
        var finalFileId = await Store.CreateFinalFileAsync([partialId], metadata: null, AbortToken);

        // then
        (await ReadContentAsync(finalFileId))
            .Should()
            .BeEquivalentTo(data);
    }

    public virtual async Task should_allow_same_partial_multiple_times_in_final_file()
    {
        // given - the tus spec: partials "MAY however be used multiple times to form a final
        // resource", including repeated entries in one Upload-Concat list
        var data = Faker.Random.Bytes(400);
        var partialId = await Store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await Store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when - the same partial is listed twice
        var finalFileId = await Store.CreateFinalFileAsync([partialId, partialId], metadata: null, AbortToken);

        // then - its content appears twice, in order
        (await ReadContentAsync(finalFileId))
            .Should()
            .BeEquivalentTo(data.Concat(data).ToArray());
        (await Store.GetUploadLengthAsync(finalFileId, AbortToken)).Should().Be(data.Length * 2);
    }

    public virtual async Task should_keep_partials_after_concat_by_default()
    {
        // given
        var data = Faker.Random.Bytes(250);
        var partialId = await Store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await Store.AppendDataAsync(partialId, stream, AbortToken);
        }

        // when
        _ = await Store.CreateFinalFileAsync([partialId], metadata: null, AbortToken);

        // then - the spec allows reusing partials for multiple finals; the default keeps them
        (await Store.FileExistAsync(partialId, AbortToken))
            .Should()
            .BeTrue();
    }

    public virtual async Task should_preserve_user_metadata_in_final_file()
    {
        // given
        var data = Faker.Random.Bytes(300);
        var partialId = await Store.CreatePartialFileAsync(data.Length, metadata: null, AbortToken);

        await using (var stream = new MemoryStream(data))
        {
            await Store.AppendDataAsync(partialId, stream, AbortToken);
        }

        var fileName = Faker.System.FileName();
        var customValue = Faker.Random.AlphaNumeric(10);
        var metadata = $"filename {fileName.ToBase64()},customkey {customValue.ToBase64()}";

        // when
        var finalFileId = await Store.CreateFinalFileAsync([partialId], metadata, AbortToken);

        // then
        var retrievedMetadata = await Store.GetUploadMetadataAsync(finalFileId, AbortToken);
        retrievedMetadata.Should().Contain("filename");
        retrievedMetadata.Should().Contain("customkey");
    }
}
