using System.Xml.Linq;
using Framework.Arguments;
using Framework.Blobs;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.DataProtection;

/// <summary>An <see cref="IXmlRepository"/> which is backed by BlobStorage.</summary>
/// <remarks>Instances of this type are thread-safe.</remarks>
public sealed class BlobStorageDataProtectionXmlRepository : IXmlRepository
{
    private static readonly string[] _Containers = ["DataProtection"];
    private readonly IBlobStorage _storage;
    private readonly ILogger _logger;

    public BlobStorageDataProtectionXmlRepository(IBlobStorage storage, ILoggerFactory? loggerFactory = null)
    {
        Argument.IsNotNull(storage);
        _storage = storage;
        _logger = loggerFactory?.CreateLogger(typeof(BlobStorageDataProtectionXmlRepository)) ?? NullLogger.Instance;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        return _GetAllElementsAsync().GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyCollection<XElement>> _GetAllElementsAsync()
    {
        _logger.LogTrace("Loading elements...");

        var files = (await _storage.GetFileListAsync(_Containers, "*.xml")).ToList();

        if (files.Count == 0)
        {
            _logger.LogTrace("No elements were found");

            return [];
        }

        _logger.LogTrace("Found {FileCount} elements", files.Count);

        var elements = new List<XElement>(files.Count);

        foreach (var file in files)
        {
            _logger.LogTrace("Loading element: {File}", file.Path);
            var downloadResult = await _storage.DownloadAsync(file.Path, _Containers);

            if (downloadResult == null)
            {
                _logger.LogWarning("Failed to load element: {File}", file.Path);

                continue;
            }

            await using (var stream = downloadResult.Stream)
            {
                elements.Add(XElement.Load(stream));
            }

            _logger.LogTrace("Loaded element: {File}", file.Path);
        }

        return elements.AsReadOnly();
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string? friendlyName)
    {
        Argument.IsNotNull(element);

        _StoreElementAsync(element, friendlyName).GetAwaiter().GetResult();
    }

    private Task _StoreElementAsync(XElement element, string? friendlyName)
    {
        var fileName = string.Concat(
            !string.IsNullOrEmpty(friendlyName) ? friendlyName : Guid.NewGuid().ToString("N"),
            ".xml"
        );

        _logger.LogTrace("Saving element: {File}", fileName);

        return Run.WithRetriesAsync(async () =>
        {
            using var memoryStream = new MemoryStream();
            element.Save(memoryStream, SaveOptions.DisableFormatting);
            memoryStream.Seek(0, SeekOrigin.Begin);
            // tODO: the upload will override the file name
            _ = await _storage.UploadAsync(new BlobUploadRequest(memoryStream, fileName), _Containers);
            _logger.LogTrace("Saved element: {File}", fileName);
        });
    }
}
