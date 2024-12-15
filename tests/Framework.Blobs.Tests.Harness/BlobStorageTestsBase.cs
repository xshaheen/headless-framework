// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Xml.Linq;
using Framework.Blobs;
using Framework.BuildingBlocks.Helpers.System;
using Framework.Testing.Helpers;

namespace Tests;

public abstract class BlobStorageTestsBase(ITestOutputHelper output)
{
    protected abstract IBlobStorage GetStorage();

    protected static string ContainerName => "storage";

    protected static string[] Container => [ContainerName];

    public virtual async Task CanGetEmptyFileListOnMissingDirectoryAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var list = await storage.GetBlobsListAsync(Container, $"{Guid.NewGuid()}\\*");

        list.Should().BeEmpty();
    }

    public virtual async Task CanGetFileListForSingleFolderAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var name = ContainerName;
        var container = Container;

        await storage.UploadContentAsync([name, "archived"], "archived.txt", "archived");
        await storage.UploadContentAsync([name, "q"], "new.txt", "new");
        await storage.UploadContentAsync([name, "long", "path", "in", "here"], "1.hey.stuff-2.json", "archived");

        (await storage.GetBlobsListAsync(container)).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, limit: 1)).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"long\path\in\here\*stuff*.json")).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"archived\*")).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"q\*")).Should().ContainSingle();
        (await storage.GetBlobContentAsync([name, "archived"], "archived.txt")).Should().Be("archived");
        (await storage.GetBlobContentAsync([name, "q"], "new.txt")).Should().Be("new");
        (await storage.GetBlobsListAsync(container, @"q\*")).Should().ContainSingle();
    }

    public virtual async Task CanGetFileListForSingleFileAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var name = ContainerName;

        await storage.UploadContentAsync([name, "archived"], "archived.txt", "archived");
        await storage.UploadContentAsync([name, "archived"], "archived.csv", "archived");
        await storage.UploadContentAsync([name, "q"], "new.txt", "new");
        await storage.UploadContentAsync([name, "long", "path", "in", "here"], "1.hey.stuff-2.json", "archived");

        var list = await storage.GetBlobsListAsync([name, "archived"], "archived.txt");

        list.Should().ContainSingle();
        list[0].BlobKey.Should().Be("archived/archived.txt");
        list[0].Size.Should().BePositive();
        list[0].Created.Should().BeAfter(DateTimeOffset.MinValue);
    }

    public virtual async Task CanGetPagedFileListForSingleFolderAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var name = ContainerName;
        var container = Container;

        // Should be empty
        var result = await storage.GetPagedListAsync(container, pageSize: 1);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        (await result.NextPageAsync()).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();

        // Add one
        await storage.UploadContentAsync([name, "archived"], "archived.txt", "archived");

        // Should have one
        result = await storage.GetPagedListAsync([name], pageSize: 1);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().ContainSingle();
        (await result.NextPageAsync()).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().ContainSingle();

        // Add another
        await storage.UploadContentAsync([name, "q"], "new.txt", "new");

        // Should have two
        result = await storage.GetPagedListAsync([name], pageSize: 1);
        result.HasMore.Should().BeTrue();
        result.Blobs.Should().ContainSingle();
        (await result.NextPageAsync()).Should().BeTrue();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().ContainSingle();

        string[] longContainer = [name, "long", "path", "in", "here"];
        await storage.UploadContentAsync(longContainer, "1.hey.stuff-2.json", "long data");

        (await storage.GetPagedListAsync(container, pageSize: 100)).Blobs.Should().HaveCount(3);
        (await storage.GetPagedListAsync(container, pageSize: 1)).Blobs.Should().ContainSingle();

        var list = await storage.GetPagedListAsync(container, @"long\path\in\here\*stuff*.json", pageSize: 2);
        list.Blobs.Should().ContainSingle();
        (await storage.GetBlobContentAsync(longContainer, "1.hey.stuff-2.json")).Should().Be("long data");

        list = await storage.GetPagedListAsync(container, blobSearchPattern: @"archived\*", pageSize: 2);
        list.Blobs.Should().ContainSingle();
        (await storage.GetBlobContentAsync([name, "archived"], "archived.txt")).Should().Be("archived");

        list = await storage.GetPagedListAsync(container, blobSearchPattern: @"q\*", pageSize: 2);
        list.Blobs.Should().ContainSingle();
        (await storage.GetBlobContentAsync([name, "q"], "new.txt")).Should().Be("new");
    }

    public virtual async Task CanGetFileInfoAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [ContainerName, "folder"];

        /* Not exist */
        var fileInfo = await storage.GetBlobInfoAsync(container, Guid.NewGuid().ToString());
        fileInfo.Should().BeNull();

        /* Exist one */
        var startTime = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1));
        var blobName = $"{Guid.NewGuid()}-nested.txt";
        await storage.UploadContentAsync(container, blobName, "test");

        fileInfo = await storage.GetBlobInfoAsync(container, blobName);
        fileInfo.Should().NotBeNull();
        fileInfo!.BlobKey.Should().Be($"folder/{blobName}");
        fileInfo.Size.Should().BePositive("Should have file size");

        // NOTE: File creation time might not be accurate:
        // http://stackoverflow.com/questions/2109152/unbelievable-strange-file-creation-time-problem

        fileInfo
            .Created.Should()
            .BeAfter(DateTimeOffset.MinValue, "File creation time should be newer than the start time");

        fileInfo
            .Modified.Should()
            .BeOnOrAfter(
                startTime,
                $"File {blobName} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );

        /* Exist multiple */
        blobName = $"{Guid.NewGuid()}-test.txt";
        await storage.UploadContentAsync(container, blobName, "test");
        fileInfo = await storage.GetBlobInfoAsync(container, blobName);

        fileInfo.Should().NotBeNull();
        fileInfo!.BlobKey.Should().EndWith($"folder/{blobName}", "Incorrect file");
        fileInfo.Size.Should().BePositive("Incorrect file size");

        fileInfo
            .Created.Should()
            .BeOnOrAfter(DateTimeOffset.MinValue, "File creation time should be newer than the start time.");

        fileInfo
            .Modified.Should()
            .BeOnOrAfter(
                startTime,
                $"File {blobName} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );
    }

    public virtual async Task CanGetNonExistentFileInfoAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;

        // ReSharper disable once AccessToDisposedClosure
        Func<Task> action = async () => _ = await storage.GetBlobInfoAsync(container, null!);
        await action.Should().ThrowExactlyAsync<ArgumentNullException>();
        (await storage.GetBlobInfoAsync(container, Guid.NewGuid().ToString())).Should().BeNull();
    }

    public virtual async Task CanManageFilesAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var name = ContainerName;

        await storage.UploadContentAsync(container, "test.txt", "test");
        var file = (await storage.GetBlobsListAsync(container)).Single();
        file.Should().NotBeNull();
        file.BlobKey.Should().Be("test.txt");

        var content = await storage.GetBlobContentAsync(container, "test.txt");
        content.Should().Be("test");
        (await storage.RenameAsync(container, "test.txt", container, "new.txt")).Should().BeTrue();
        (await storage.GetBlobsListAsync(container)).Should().ContainSingle(x => x.BlobKey == "new.txt");
        (await storage.DeleteAsync(container, "new.txt")).Should().BeTrue();
        (await storage.GetBlobsListAsync(container)).Should().BeEmpty();
    }

    public virtual async Task CanRenameFilesAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var name = ContainerName;

        // Rename & Move
        await storage.UploadContentAsync(container, "test.txt", "test");
        (await storage.RenameAsync(container, "test.txt", [name, "archive"], "new.txt")).Should().BeTrue();
        (await storage.GetBlobContentAsync([name, "archive"], "new.txt")).Should().Be("test");
        (await storage.GetBlobsListAsync(container)).Should().ContainSingle();

        // Rename & Overwrite
        await storage.UploadContentAsync(container, "test2.txt", "test2");
        (await storage.RenameAsync(container, "test2.txt", [name, "archive"], "new.txt")).Should().BeTrue();
        (await storage.GetBlobContentAsync([name, "archive"], "new.txt")).Should().Be("test2");
        (await storage.GetBlobsListAsync(container)).Should().ContainSingle();
    }

    public virtual async Task CanDeleteEntireFolderAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var containerName = ContainerName;

        await storage.UploadContentAsync([containerName, "x"], "hello.txt", "hello");
        await storage.UploadContentAsync([containerName, "x", "nested"], "world.csv", "nested world");
        (await storage.GetBlobsListAsync(container)).Should().HaveCount(2);
        (await storage.DeleteAllAsync(container, @"x\*")).Should().Be(2);
        (await storage.GetBlobsListAsync(container)).Should().BeEmpty();
    }

    public virtual async Task CanDeleteEntireFolderWithWildcardAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var containerName = ContainerName;

        await storage.UploadContentAsync([containerName, "x"], "hello.txt", "hello");
        await storage.UploadContentAsync([containerName, "x", "nested"], "world.csv", "nested world");
        (await storage.GetBlobsListAsync(container)).Should().HaveCount(2);
        (await storage.GetBlobsListAsync(container, limit: 1)).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, blobSearchPattern: @"x\*")).Should().HaveCount(2);
        (await storage.GetBlobsListAsync(container, blobSearchPattern: @"x\nested\*")).Should().ContainSingle();

        await storage.DeleteAllAsync(container, @"x\*");
        (await storage.GetBlobsListAsync(container)).Should().BeEmpty();
    }

    public virtual async Task CanDeleteFolderWithMultiFolderWildcardsAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var name = ContainerName;
        const int filesPerMonth = 5;

        for (var year = 2020; year <= 2021; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                for (var index = 0; index < filesPerMonth; index++)
                {
                    await storage.UploadContentAsync(
                        [
                            name,
                            "archive",
                            $"year-{year.ToString("00", CultureInfo.InvariantCulture)}",
                            $"month-{month.ToString("00", CultureInfo.InvariantCulture)}",
                        ],
                        $"file-{index.ToString("00", CultureInfo.InvariantCulture)}.txt",
                        "hello"
                    );
                }
            }
        }

        output.WriteLine(@"List by pattern: archive\*");
        (await storage.GetBlobsListAsync(container, @"archive\*")).Should().HaveCount(2 * 12 * filesPerMonth);

        output.WriteLine(@"List by pattern: archive\*month-01*");
        (await storage.GetBlobsListAsync(container, @"archive\*month-01*")).Should().HaveCount(2 * filesPerMonth);

        output.WriteLine(@"List by pattern: archive\year-2020\*month-01*");

        (await storage.GetBlobsListAsync(container, @"archive\year-2020\*month-01*")).Should().HaveCount(filesPerMonth);

        output.WriteLine(@"Delete by pattern: archive\*month-01*");
        await storage.DeleteAllAsync(container, @"archive\*month-01*");
        (await storage.GetBlobsListAsync(container)).Should().HaveCount(2 * 11 * filesPerMonth);
    }

    public virtual async Task CanDeleteSpecificFilesAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var name = ContainerName;

        await storage.UploadContentAsync([name, "x"], "hello.txt", "hello");
        await storage.UploadContentAsync([name, "x", "nested"], "world.csv", "nested world");
        await storage.UploadContentAsync([name, "x", "nested"], "hello.txt", "nested hello");

        (await storage.GetBlobsListAsync(container)).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, limit: 1)).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"x\*")).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, @"x\nested\*")).Should().HaveCount(2);
        (await storage.GetBlobsListAsync(container, @"x\*.txt")).Should().HaveCount(2);

        await storage.DeleteAllAsync(container, @"x\*.txt");

        (await storage.GetBlobsListAsync(container)).Should().ContainSingle();
        (await storage.ExistsAsync([name, "x"], "hello.txt")).Should().BeFalse();
        (await storage.ExistsAsync([name, "x", "nested"], "hello.txt")).Should().BeFalse();
        (await storage.ExistsAsync([name, "x", "nested"], "world.csv")).Should().BeTrue();
    }

    public virtual async Task CanDeleteNestedFolderAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var name = ContainerName;
        var container = Container;

        await storage.UploadContentAsync([name, "x"], "hello.txt", "hello");
        await storage.UploadContentAsync([name, "x", "nested"], "world.csv", "nested world");
        await storage.UploadContentAsync([name, "x", "nested"], "hello.txt", "nested hello");
        (await storage.GetBlobsListAsync(container)).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, limit: 1)).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"x\*")).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, @"x\nested\*")).Should().HaveCount(2);
        (await storage.GetBlobsListAsync(container, @"x\*.txt")).Should().HaveCount(2);

        await storage.DeleteAllAsync(container, @"x\nested\*");

        (await storage.GetBlobsListAsync(container)).Should().ContainSingle();
        (await storage.ExistsAsync([name, "x"], "hello.txt")).Should().BeTrue();
        (await storage.ExistsAsync([name, "x", "nested"], "hello.txt")).Should().BeFalse();
        (await storage.ExistsAsync([name, "x", "nested"], "world.csv")).Should().BeFalse();
    }

    public virtual async Task CanDeleteSpecificFilesInNestedFolderAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var name = ContainerName;
        var container = Container;

        await storage.UploadContentAsync([name, "x"], "hello.txt", "hello");
        await storage.UploadContentAsync([name, "x"], "world.csv", "world");
        await storage.UploadContentAsync([name, "x", "nested"], "world.csv", "nested world");
        await storage.UploadContentAsync([name, "x", "nested"], "hello.txt", "nested hello");
        await storage.UploadContentAsync([name, "x", "nested"], "again.txt", "nested again");

        (await storage.GetBlobsListAsync(container)).Should().HaveCount(5);
        (await storage.GetBlobsListAsync(container, limit: 1)).Should().ContainSingle();
        (await storage.GetBlobsListAsync(container, @"x\*")).Should().HaveCount(5);
        (await storage.GetBlobsListAsync(container, @"x\nested\*")).Should().HaveCount(3);
        (await storage.GetBlobsListAsync(container, @"x\*.txt")).Should().HaveCount(3);

        await storage.DeleteAllAsync(container, @"x\nested\*.txt");
        (await storage.GetBlobsListAsync(container)).Should().HaveCount(3);
        (await storage.ExistsAsync([name, "x"], "hello.txt")).Should().BeTrue();
        (await storage.ExistsAsync([name, "x"], "world.csv")).Should().BeTrue();
        (await storage.ExistsAsync([name, "x", "nested"], "hello.txt")).Should().BeFalse();
        (await storage.ExistsAsync([name, "x", "nested"], "again.txt")).Should().BeFalse();
        (await storage.ExistsAsync([name, "x", "nested"], "world.csv")).Should().BeTrue();
    }

    public virtual async Task CanRoundTripSeekableStreamAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        const string path = "user.xml";
        var container = Container;

        // Create a stream of XML
        var element = XElement.Parse("<user>Blake</user>");
        await using var memoryStream = new MemoryStream();
        output.WriteLine("Saving xml to stream with position {0}.", memoryStream.Position);
        await element.SaveAsync(memoryStream, SaveOptions.DisableFormatting, CancellationToken.None);
        memoryStream.Seek(0, SeekOrigin.Begin);

        // Save the stream to storage
        output.WriteLine("Saving contents with position {0}", memoryStream.Position);
        await storage.UploadAsync(container, path, memoryStream);
        output.WriteLine("Saved contents with position {0}.", memoryStream.Position);

        // Download the stream from storage
        var downloadResult = await storage.DownloadAsync(container, path);
        downloadResult.Should().NotBeNull();
        await using var stream = downloadResult!.Stream;
        var actual = XElement.Load(stream);
        actual.ToString(SaveOptions.DisableFormatting).Should().Be(element.ToString(SaveOptions.DisableFormatting));
    }

    public virtual async Task WillRespectStreamOffsetAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        const string blobName = "blake.txt";
        var container = Container;

        await using var memoryStream = new MemoryStream();

        long offset;

        await using (var writer = new StreamWriter(memoryStream, StringHelper.Utf8WithoutBom, 1024, true))
        {
            writer.AutoFlush = true;
            await writer.WriteAsync("Eric");
            offset = memoryStream.Position;
            await writer.WriteAsync("Blake");
            await writer.FlushAsync();
        }

        memoryStream.Seek(offset, SeekOrigin.Begin);
        var blob = new BlobUploadRequest(memoryStream, blobName);
        await storage.UploadAsync(container, blob);

        (await storage.GetBlobContentAsync(container, blobName)).Should().Be("Blake");
    }

    public virtual async Task CanConcurrentlyManageFilesAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;

        // Ensure is working
        var info = await storage.GetBlobInfoAsync(container, "nope");
        info.Should().BeNull();

        string[] queueContainer = [ContainerName, "q"];
        using var queueItems = new BlockingCollection<int>();

        // Parallel Upload 10 files
        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            async (i, cancellationToken) =>
            {
                var projectId = i.ToString(CultureInfo.InvariantCulture);

                var post = new Post
                {
                    ApiVersion = 2,
                    CharSet = "utf8",
                    ContentEncoding = "application/json",
                    Data = "{}"u8.ToArray(),
                    IpAddress = "127.0.0.1",
                    MediaType = "gzip",
                    ProjectId = projectId,
                    UserAgent = "test",
                };

                await storage.UploadContentAsync(
                    queueContainer,
                    $"{projectId}.json",
                    post,
                    cancellationToken: cancellationToken
                );

                queueItems.Add(i, cancellationToken);
            }
        );

        (await storage.GetBlobsListAsync(container)).Should().HaveCount(10);

        await Parallel.ForEachAsync(
            Enumerable.Range(1, 10),
            async (_, _) =>
            {
                var blobName = Random.Shared.GetItem(queueItems).ToString(CultureInfo.InvariantCulture) + ".json";

                var eventPost = await _GetPostAndSetWorkMarkAsync(
                    storage,
                    container,
                    TestConstants.F.Random.Int(0, 25).ToString(CultureInfo.InvariantCulture) + ".json",
                    output
                );

                if (eventPost == null)
                {
                    return;
                }

                if (TestConstants.F.Random.Bool())
                {
                    await _CompletePostAsync(
                        storage,
                        container,
                        blobName,
                        eventPost.ProjectId,
                        DateTime.UtcNow,
                        true,
                        output
                    );
                }
                else
                {
                    await _DeleteWorkMarkerAsync(storage, container, blobName, output);
                }
            }
        );
    }

    public virtual async Task CanSaveOverExistingStoredContent()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        const string blobName = "test.json";

        var longIdPost = new Post { ProjectId = "1234567890" };
        await storage.UploadContentAsync(container, blobName, longIdPost);
        (await storage.GetBlobContentAsync<Post>(container, blobName)).Should().BeEquivalentTo(longIdPost);

        var shortIdPost = new Post { ProjectId = "123" };
        await storage.UploadContentAsync(container, blobName, shortIdPost);
        (await storage.GetBlobContentAsync<Post>(container, blobName)).Should().BeEquivalentTo(shortIdPost);
    }

    public virtual async Task CanCallDeleteAllAsyncWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.DeleteAllAsync(container).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallDeleteWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.DeleteAsync(container, blobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallBulkDeleteWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];
        string[] blobNames = [TestConstants.F.Random.String2(5, 25)];

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.BulkDeleteAsync(container, blobNames).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallRenameWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] sourceContainer = [TestConstants.F.Random.String2(5, 25)];
        string[] destinationContainer = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);
        var newBlobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.RenameAsync(sourceContainer, blobName, destinationContainer, newBlobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallCopyWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] sourceContainer = [TestConstants.F.Random.String2(5, 25)];
        string[] destinationContainer = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);
        var newBlobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.CopyAsync(sourceContainer, blobName, destinationContainer, newBlobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallExistsWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.ExistsAsync(container, blobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallDownloadWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.DownloadAsync(container, blobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallGetBlobInfoWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];
        var blobName = TestConstants.F.Random.String2(5, 25);

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.GetBlobInfoAsync(container, blobName).AsTask();

        await action.Should().NotThrowAsync();
    }

    public virtual async Task CanCallGetPagedListWithEmptyContainerAsync()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        string[] container = [TestConstants.F.Random.String2(5, 25)];

        // ReSharper disable once AccessToDisposedClosure
        var action = () => storage.GetPagedListAsync(container).AsTask();

        await action.Should().NotThrowAsync();
    }

    protected async Task ResetAsync(IBlobStorage? storage)
    {
        if (storage is null)
        {
            return;
        }

        var containers = Container;

        output.WriteLine("Deleting all files...");
        await storage.DeleteAllAsync(containers);

        output.WriteLine("Asserting empty files...");
        var list = await storage.GetBlobsListAsync(containers, limit: 10000);

        list.Should().BeEmpty();
    }

    #region Helpers

    private static async Task _AddWorkMarkerIfNotExistAsync(IBlobStorage storage, string[] container, string blobName)
    {
        var markerName = blobName + ".x";

        if (!await storage.ExistsAsync(container, markerName))
        {
            await storage.UploadContentAsync(container, markerName, string.Empty);
        }
    }

    private static async Task _DeleteWorkMarkerAsync(
        IBlobStorage storage,
        string[] container,
        string blobName,
        ITestOutputHelper? logger = null
    )
    {
        try
        {
            var markerName = blobName + ".x";
            _ = await storage.DeleteAsync(container, markerName);
        }
        catch (Exception e)
        {
            logger?.WriteLine($"Error deleting work marker {blobName}: {e.ExpandExceptionMessage()}");
        }
    }

    private static async Task<Post?> _GetPostAndSetWorkMarkAsync(
        IBlobStorage storage,
        string[] container,
        string blobName,
        ITestOutputHelper? logger = null
    )
    {
        Post? eventPost;

        try
        {
            eventPost = await storage.GetBlobContentAsync<Post?>(container, blobName);

            if (eventPost is null)
            {
                return null;
            }

            await _AddWorkMarkerIfNotExistAsync(storage, container, blobName);
        }
        catch (Exception e)
        {
            logger?.WriteLine($"Error retrieving event post data {blobName}: {e.ExpandExceptionMessage()}");

            return null;
        }

        return eventPost;
    }

    private static async Task _CompletePostAsync(
        IBlobStorage storage,
        string[] container,
        string blobName,
        string? projectId,
        DateTime created,
        bool shouldArchive = true,
        ITestOutputHelper? logger = null
    )
    {
        // don't move files that are already in the archive
        if (blobName.StartsWith("archive", StringComparison.Ordinal))
        {
            return;
        }

        var archivePath =
            $@"archive\{projectId}\{created.ToString(@"yy\\MM\\dd", CultureInfo.InvariantCulture)}\{Path.GetFileName(blobName)}";

        try
        {
            if (shouldArchive)
            {
                if (!await storage.RenameAsync(container, blobName, container, archivePath))
                {
                    return;
                }
            }
            else
            {
                if (!await storage.DeleteAsync(container, blobName))
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"Error archiving event post data {blobName}: {ex.ExpandExceptionMessage()}");

            return;
        }

        await _DeleteWorkMarkerAsync(storage, container, blobName);
    }

    private sealed record Post
    {
        public int ApiVersion { get; init; }

        public string? CharSet { get; init; }

        public string? ContentEncoding { get; init; }

        public byte[]? Data { get; init; }

        public string? IpAddress { get; init; }

        public string? MediaType { get; init; }

        public string? ProjectId { get; init; }

        public string? UserAgent { get; init; }
    }

    #endregion
}
