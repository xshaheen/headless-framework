using Framework.Arguments;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

public static class FormFileExtensions
{
    /// <summary>Save a the file to the <paramref name="directoryPath"/> and return file name.</summary>
    /// <param name="formFile">File to be saved</param>
    /// <param name="directoryPath">The directory to save the file to.</param>
    /// <param name="token"></param>
    public static async ValueTask<(string SavedName, string DisplayName, long Size)> SaveAsync(
        this IFormFile formFile,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(formFile);

        await using var blobStream = formFile.OpenReadStream();

        return await blobStream.SaveToLocalFileAsync(formFile.FileName, directoryPath, token);
    }

    public static async ValueTask<(string SavedName, string DisplayName, long Size)[]> SaveAsync(
        this IReadOnlyCollection<IFormFile> files,
        string directoryPath,
        CancellationToken token = default
    )
    {
        var tasks = files.Select(async formFile =>
        {
            var blobResponse = await formFile.SaveAsync(directoryPath, token);

            return blobResponse;
        });

        var result = await Task.WhenAll(tasks);

        return result;
    }

    public static async ValueTask<string> CalculateMd5Async(
        this IFormFile file,
        CancellationToken cancellationToken = default
    )
    {
        await using var stream = file.OpenReadStream();

        return await stream.CalculateMd5Async(cancellationToken);
    }
}
