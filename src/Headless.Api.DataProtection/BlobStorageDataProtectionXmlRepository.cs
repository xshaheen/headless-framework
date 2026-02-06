// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Blobs;
using Headless.Checks;
using Headless.Threading;
using Humanizer;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Headless.Api;

/// <summary>An <see cref="IXmlRepository"/> which is backed by BlobStorage.</summary>
/// <remarks>Instances of this type are thread-safe.</remarks>
public sealed class BlobStorageDataProtectionXmlRepository : IXmlRepository
{
    private static readonly string[] _Containers = ["DataProtection"];
    private readonly IBlobStorage _storage;
    private readonly ILogger _logger;

    private static readonly ResiliencePipeline _RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(
            new()
            {
                Name = "BlobStorageDataProtectionXmlRepositoryRetryPolicy",
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                Delay = 200.Milliseconds(),
                ShouldHandle = new PredicateBuilder().Handle<IOException>(),
            }
        )
        .Build();

    public BlobStorageDataProtectionXmlRepository(IBlobStorage storage, ILoggerFactory? loggerFactory = null)
    {
        Argument.IsNotNull(storage);
        _storage = storage;
        _logger = loggerFactory?.CreateLogger(typeof(BlobStorageDataProtectionXmlRepository)) ?? NullLogger.Instance;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        return Async.RunSync(_GetAllElementsAsync);
    }

    private async Task<IReadOnlyCollection<XElement>> _GetAllElementsAsync()
    {
        _logger.LogLoadingElements();

        var files = (await _storage.GetBlobsListAsync(_Containers, "*.xml")).ToList();

        if (files.Count == 0)
        {
            _logger.LogNoElementsFound();

            return [];
        }

        _logger.LogFoundElements(files.Count);

        var elements = new List<XElement>(files.Count);

        foreach (var file in files)
        {
            _logger.LogLoadingElement(file.BlobKey);
            await using var downloadResult = await _storage.OpenReadStreamAsync(_Containers, file.BlobKey);

            if (downloadResult is null)
            {
                _logger.LogFailedToLoadElement(file.BlobKey);

                continue;
            }

            elements.Add(XElement.Load(downloadResult.Stream));

            _logger.LogLoadedElement(file.BlobKey);
        }

        return elements.AsReadOnly();
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string? friendlyName)
    {
        Argument.IsNotNull(element);
        var fileName = string.IsNullOrEmpty(friendlyName) ? $"{Guid.NewGuid():N}.xml" : $"{friendlyName}.xml";

        Async.RunSync(() => _StoreElementAsync(element, fileName));
    }

    private async Task _StoreElementAsync(XElement element, string fileName)
    {
        _logger.LogSavingElement(fileName);
        await _RetryPipeline.ExecuteAsync(storeElementAsync, (_storage, element, fileName));
        _logger.LogSavedElement(fileName);

        return;

        static async ValueTask storeElementAsync(
            (IBlobStorage Storage, XElement Element, string FileName) state,
            CancellationToken cancellationToken
        )
        {
            var (storage, element, fileName) = state;

            await using var memoryStream = new MemoryStream();
            await element.SaveAsync(memoryStream, SaveOptions.DisableFormatting, cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);

            await storage.UploadAsync(_Containers, new(memoryStream, fileName), cancellationToken);
        }
    }
}
