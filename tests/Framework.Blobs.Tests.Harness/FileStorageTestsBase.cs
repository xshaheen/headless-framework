// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;

namespace Tests;

public abstract class FileStorageTestsBase(ITestOutputHelper output)
{
    protected abstract IBlobStorage? GetStorage();

    protected string GetContainer() => "storage";

    protected string[] GetContainers() => [GetContainer()];

    public virtual async Task CanGetEmptyFileListOnMissingDirectoryAsync()
    {
        var storage = GetStorage();

        if (storage is null)
        {
            return;
        }

        await ResetAsync(storage);

        using (storage)
        {
            var list = await storage.GetFileListAsync(GetContainers(), $"{Guid.NewGuid()}\\*");

            list.Should().BeEmpty();
        }
    }

    public virtual async Task CanGetFileListForSingleFolderAsync()
    {
        var storage = GetStorage();

        if (storage is null)
        {
            return;
        }

        await ResetAsync(storage);

        var container = GetContainer();
        var containers = GetContainers();

        using (storage)
        {
            await storage.UploadAsync([container, "archived"], "archived.txt", "archived");
            await storage.UploadAsync([container, "q"], "new.txt", "new");
            await storage.UploadAsync([container, "long", "path", "in", "here"], "1.hey.stuff-2.json", "archived");

            (await storage.GetFileListAsync(containers)).Should().HaveCount(3);
            (await storage.GetFileListAsync(containers, limit: 1)).Should().ContainSingle();
            (await storage.GetFileListAsync(containers, @"long\path\in\here\*stuff*.json")).Should().ContainSingle();
            (await storage.GetFileListAsync(containers, @"archived\*")).Should().ContainSingle();
            (await storage.GetFileListAsync(containers, @"q\*")).Should().ContainSingle();
            (await storage.GetFileContentsAsync([container, "archived"], "archived.txt")).Should().Be("archived");
            (await storage.GetFileContentsAsync([container, "q"], "new.txt")).Should().Be("new");
            (await storage.GetFileListAsync(containers, @"q\*")).Should().ContainSingle();
        }
    }

    public virtual async Task CanGetFileListForSingleFileAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        var container = GetContainer();

        using (storage)
        {
            await storage.UploadAsync([container, "archived"], "archived.txt", "archived");
            await storage.UploadAsync([container, "archived"], "archived.csv", "archived");
            await storage.UploadAsync([container, "q"], "new.txt", "new");
            await storage.UploadAsync([container, "long", "path", "in", "here"], "1.hey.stuff-2.json", "archived");

            var list = await storage.GetFileListAsync([container, "archived"], "archived.txt");

            list.Should().ContainSingle();
            list[0].Path.Should().Be("storage/archived/archived.txt");
            list[0].Size.Should().BePositive();
            list[0].Created.Should().BeAfter(DateTime.MinValue);
        }
    }

    public virtual async Task CanGetPagedFileListForSingleFolderAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        var container = GetContainer();

        using (storage)
        {
            var result = await storage.GetPagedListAsync([container], pageSize: 1);

            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();
            (await result.NextPageAsync()).Should().BeFalse();
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();

            await storage.UploadAsync([container, "archived"], "archived.txt", "archived");
            result = await storage.GetPagedListAsync([container], pageSize: 1);

            result.HasMore.Should().BeFalse();
            result.Blobs.Should().ContainSingle();
            (await result.NextPageAsync()).Should().BeFalse();
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().ContainSingle();

            await storage.UploadAsync([container, "q"], "new.txt", "new");
            result = await storage.GetPagedListAsync([container], pageSize: 1);

            result.HasMore.Should().BeTrue();
            result.Blobs.Should().ContainSingle();
            (await result.NextPageAsync()).Should().BeTrue();
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().ContainSingle();

            string[] longContainer = [container, "long", "path", "in", "here"];
            await storage.UploadAsync(longContainer, "1.hey.stuff-2.json", "long data");

            (await storage.GetPagedListAsync([container], pageSize: 100)).Blobs.Should().HaveCount(3);
            (await storage.GetPagedListAsync([container], pageSize: 1)).Blobs.Should().ContainSingle();

            var list = await storage.GetPagedListAsync([container], @"long\path\in\here\*stuff*.json", pageSize: 2);
            list.Blobs.Should().ContainSingle();
            (await storage.GetFileContentsAsync(longContainer, "1.hey.stuff-2.json")).Should().Be("long data");

            list = await storage.GetPagedListAsync([container], blobSearchPattern: @"archived\*", pageSize: 2);
            list.Blobs.Should().ContainSingle();
            (await storage.GetFileContentsAsync([container, "archived"], "archived.txt")).Should().Be("archived");

            list = await storage.GetPagedListAsync([container], blobSearchPattern: @"q\*", pageSize: 2);
            list.Blobs.Should().ContainSingle();
            (await storage.GetFileContentsAsync([container, "q"], "new.txt")).Should().Be("new");
        }
    }

    public virtual async Task CanGetFileInfoAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        using (storage)
        {
            var fileInfo = await storage.GetFileInfoAsync(Guid.NewGuid().ToString());
            Assert.Null(fileInfo);

            var startTime = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(1));
            string path = $"folder\\{Guid.NewGuid()}-nested.txt";
            Assert.True(await storage.SaveFileAsync(path, "test"));
            fileInfo = await storage.GetFileInfoAsync(path);
            Assert.NotNull(fileInfo);
            Assert.True(fileInfo.Path.EndsWith("nested.txt"), "Incorrect file");
            Assert.True(fileInfo.Size > 0, "Incorrect file size");
            // NOTE: File creation time might not be accurate: http://stackoverflow.com/questions/2109152/unbelievable-strange-file-creation-time-problem
            Assert.True(fileInfo.Created > DateTime.MinValue, "File creation time should be newer than the start time");

            Assert.True(
                startTime <= fileInfo.Modified,
                $"File {path} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );

            path = $"{Guid.NewGuid()}-test.txt";
            Assert.True(await storage.SaveFileAsync(path, "test"));
            fileInfo = await storage.GetFileInfoAsync(path);
            Assert.NotNull(fileInfo);
            Assert.True(fileInfo.Path.EndsWith("test.txt"), "Incorrect file");
            Assert.True(fileInfo.Size > 0, "Incorrect file size");

            Assert.True(
                fileInfo.Created > DateTime.MinValue,
                "File creation time should be newer than the start time."
            );

            Assert.True(
                startTime <= fileInfo.Modified,
                $"File {path} modified time {fileInfo.Modified:O} should be newer than the start time {startTime:O}."
            );
        }
    }

    public virtual async Task CanGetNonExistentFileInfoAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        using (storage)
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.GetFileInfoAsync(null));
            Assert.Null(await storage.GetFileInfoAsync(Guid.NewGuid().ToString()));
        }
    }

    public virtual async Task CanManageFilesAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        var mainContainer = GetContainers();
        var mainName = GetContainer();

        using (storage)
        {
            await storage.UploadAsync(mainContainer, "test.txt", "test");
            var file = (await storage.GetFileListAsync(mainContainer)).Single();
            file.Should().NotBeNull();
            file.Path.Should().Be("test.txt");
            var content = await storage.GetFileContentsAsync(mainContainer, "test.txt");
            content.Should().Be("test");
            await storage.RenameAsync("test.txt", "new.txt");
            Assert.Contains(await storage.GetFileListAsync(), f => f.Path == "new.txt");
            await storage.DeleteFileAsync("new.txt");
            Assert.Empty(await storage.GetFileListAsync());
        }
    }

    public virtual async Task CanRenameFilesAsync()
    {
        var storage = GetStorage();

        if (storage == null)
        {
            return;
        }

        await ResetAsync(storage);

        var mainContainer = GetContainers();
        var mainName = GetContainer();

        using (storage)
        {
            // Rename & Move
            await storage.UploadAsync(mainContainer, "test.txt", "test");
            (await storage.RenameAsync(mainContainer, "test.txt", [mainName, "archive"], "new.txt")).Should().BeTrue();
            (await storage.GetFileContentsAsync([mainName, "archive"], "new.txt")).Should().Be("test");
            (await storage.GetFileListAsync(mainContainer)).Should().ContainSingle();

            // Rename & Overwrite
            await storage.UploadAsync(mainContainer, "test2.txt", "test2");
            (await storage.RenameAsync(mainContainer, "test2.txt", [mainName, "archive"], "new.txt")).Should().BeTrue();
            (await storage.GetFileContentsAsync([mainName, "archive"], "new.txt")).Should().Be("test2");
            (await storage.GetFileListAsync(mainContainer)).Should().ContainSingle();
        }
    }

    protected async Task ResetAsync(IBlobStorage? storage)
    {
        if (storage is null)
        {
            return;
        }

        var containers = GetContainers();

        output.WriteLine("Deleting all files...");
        await storage.DeleteAllAsync(containers);

        output.WriteLine("Asserting empty files...");
        var list = await storage.GetFileListAsync(containers, limit: 10000);

        list.Should().BeEmpty();
    }

    public sealed record Post
    {
        public int ApiVersion { get; set; }

        public string? CharSet { get; set; }

        public string? ContentEncoding { get; set; }

        public byte[]? Data { get; set; }

        public string? IpAddress { get; set; }

        public string? MediaType { get; set; }

        public string? ProjectId { get; set; }

        public string? UserAgent { get; set; }
    }
}
