// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;

namespace Tests;

/// <summary>
/// End-to-end upload flows every tus store must support: a single-chunk upload, a resumed upload, concurrent uploads,
/// and a multi-chunk upload, each checked byte for byte through the readable store.
/// </summary>
public abstract class TusUploadFlowConformanceTestsBase<TStore>(ITusConformanceBackend<TStore> backend)
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
    /* Test #97: Complete small file upload */

    public virtual async Task should_complete_small_file_upload()
    {
        // given - create file with known content
        var content = Faker.Random.Bytes(1_000);
        var uploadLength = content.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        // when - create file
        var fileId = await Store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // then - upload single chunk
        await using (var stream = new MemoryStream(content))
        {
            var bytesWritten = await Store.AppendDataAsync(fileId, stream, AbortToken);
            bytesWritten.Should().Be(content.Length);
        }

        // and verify content matches
        (await ReadContentAsync(fileId))
            .Should()
            .BeEquivalentTo(content);

        // and verify upload is complete
        var offset = await Store.GetUploadOffsetAsync(fileId, AbortToken);
        var length = await Store.GetUploadLengthAsync(fileId, AbortToken);
        offset.Should().Be(length);
    }

    /* Test #98: Resume interrupted upload */

    public virtual async Task should_resume_interrupted_upload()
    {
        // given - create file for chunked upload
        var chunk1 = Faker.Random.Bytes(500);
        var chunk2 = Faker.Random.Bytes(700);
        var fullContent = chunk1.Concat(chunk2).ToArray();
        var uploadLength = fullContent.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        var fileId = await Store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // upload first chunk (simulating partial upload before interruption)
        await using (var stream1 = new MemoryStream(chunk1))
        {
            await Store.AppendDataAsync(fileId, stream1, AbortToken);
        }

        // when - simulate resumption: get current offset
        var currentOffset = await Store.GetUploadOffsetAsync(fileId, AbortToken);

        // then - offset should reflect first chunk
        currentOffset.Should().Be(chunk1.Length);

        // resume upload with second chunk
        await using (var stream2 = new MemoryStream(chunk2))
        {
            var bytesWritten = await Store.AppendDataAsync(fileId, stream2, AbortToken);
            bytesWritten.Should().Be(chunk2.Length);
        }

        // and verify complete file content
        (await ReadContentAsync(fileId))
            .Should()
            .BeEquivalentTo(fullContent);

        // and verify final offset equals upload length
        var finalOffset = await Store.GetUploadOffsetAsync(fileId, AbortToken);
        finalOffset.Should().Be(uploadLength);
    }

    /* Test #99: Concurrent uploads */

    public virtual async Task should_handle_concurrent_uploads()
    {
        // given - prepare multiple files for concurrent upload
        const int fileCount = 5;
        var files = Enumerable
            .Range(0, fileCount)
            .Select(_ => new
            {
                Content = Faker.Random.Bytes(Faker.Random.Number(500, 2_000)),
                FileName = Faker.System.FileName(),
            })
            .ToList();

        // when - upload all files concurrently
        var uploadTasks = files.Select(async file =>
        {
            var metadata = $"filename {file.FileName.ToBase64()}";
            var fileId = await Store.CreateFileAsync(file.Content.Length, metadata, AbortToken);

            await using var stream = new MemoryStream(file.Content);
            await Store.AppendDataAsync(fileId, stream, AbortToken);

            return new { FileId = fileId, ExpectedContent = file.Content };
        });

        var results = await Task.WhenAll(uploadTasks);

        // then - verify all files uploaded correctly
        results.Should().HaveCount(fileCount);

        foreach (var result in results)
        {
            var exists = await Store.FileExistAsync(result.FileId, AbortToken);
            exists.Should().BeTrue();

            (await ReadContentAsync(result.FileId)).Should().BeEquivalentTo(result.ExpectedContent);
        }
    }

    /* Test #100: Large file chunking */

    public virtual async Task should_handle_large_file_chunking()
    {
        // given - create file with multiple chunks
        const int chunkCount = 5;
        var chunks = Enumerable.Range(0, chunkCount).Select(_ => Faker.Random.Bytes(1_000)).ToList();

        var fullContent = chunks.SelectMany(c => c).ToArray();
        var uploadLength = fullContent.Length;
        var fileName = Faker.System.FileName();
        var metadata = $"filename {fileName.ToBase64()}";

        var fileId = await Store.CreateFileAsync(uploadLength, metadata, AbortToken);

        // when - upload each chunk sequentially (simulating chunked upload)
        var totalBytesWritten = 0L;

        foreach (var chunk in chunks)
        {
            await using var stream = new MemoryStream(chunk);
            var bytesWritten = await Store.AppendDataAsync(fileId, stream, AbortToken);

            totalBytesWritten += bytesWritten;

            // verify offset increases after each chunk
            var currentOffset = await Store.GetUploadOffsetAsync(fileId, AbortToken);
            currentOffset.Should().Be(totalBytesWritten);
        }

        // then - verify total bytes
        totalBytesWritten.Should().Be(fullContent.Length);

        // and verify final content integrity
        (await ReadContentAsync(fileId))
            .Should()
            .BeEquivalentTo(fullContent);

        // and verify upload is complete
        var finalOffset = await Store.GetUploadOffsetAsync(fileId, AbortToken);
        var length = await Store.GetUploadLengthAsync(fileId, AbortToken);
        finalOffset.Should().Be(length);
    }
}
