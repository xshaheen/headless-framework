using Framework.Blobs;

namespace Tests;

public abstract class FileStorageTestsBase(ITestOutputHelper output)
{
    protected abstract IBlobStorage? GetStorage();

    protected virtual string GetContainer() => "storage";

    protected virtual string[] GetContainers() => [GetContainer()];

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

        using (storage)
        {
            var container = GetContainer();
            var containers = GetContainers();

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

    protected virtual async Task ResetAsync(IBlobStorage? storage)
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
