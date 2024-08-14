namespace Framework.Blobs;

public static class BlobStorageExtensions
{
    public static async Task<IReadOnlyCollection<BlobSpecification>> GetFileListAsync(
        this IBlobStorage storage,
        string[] container,
        string? searchPattern = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<BlobSpecification>();

        limit ??= int.MaxValue;

        var result = await storage.GetPagedListAsync(container, searchPattern, limit.Value, cancellationToken);

        do
        {
            files.AddRange(result.Files);
        } while (result.HasMore && files.Count < limit.Value && await result.NextPageAsync());

        return files;
    }
}
