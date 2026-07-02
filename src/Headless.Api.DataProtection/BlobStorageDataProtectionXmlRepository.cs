// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;
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

/// <summary>An <see cref="IXmlRepository"/> implementation that persists ASP.NET Core Data Protection keys as XML blobs in an <see cref="IBlobStorage"/> backend.</summary>
/// <remarks>
/// <para>
/// Keys are stored under a <c>DataProtection</c> container as individual <c>*.xml</c> files.
/// On <see cref="GetAllElements"/>, every blob in that container is enumerated; blobs that cannot be
/// downloaded (e.g. deleted between the listing and the read) are silently skipped.
/// Blobs whose content is not well-formed XML (<see cref="System.Xml.XmlException"/>) or cannot be
/// read due to I/O failures are also skipped and logged at <c>Warning</c> level — they do not abort
/// the load of the remaining keys.
/// </para>
/// <para>
/// <see cref="StoreElement"/> retries transient I/O and HTTP failures up to 4 times with exponential
/// back-off and jitter before propagating the exception.
/// </para>
/// <para>Instances of this type are thread-safe.</para>
/// </remarks>
internal sealed class BlobStorageDataProtectionXmlRepository : IXmlRepository
{
    /// <summary>The top-level container all data-protection key XML blobs live in.</summary>
    internal const string ContainerName = "DataProtection";
    private readonly IBlobStorage _storage;
    private readonly IBlobContainerManager? _containerManager;
    private readonly ILogger _logger;

    private static readonly ResiliencePipeline _RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(
            new()
            {
                Name = "BlobStorageDataProtectionXmlRepositoryRetryPolicy",
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = 200.Milliseconds(),
                ShouldHandle = new PredicateBuilder().Handle<IOException>().Handle<HttpRequestException>(),
            }
        )
        .Build();

    /// <summary>Initializes a new instance that reads and writes data-protection key XML to the given blob storage.</summary>
    /// <param name="storage">The blob storage backend to read and write key XML files.</param>
    /// <param name="loggerFactory">Optional logger factory; when <see langword="null"/>, a no-op logger is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="storage"/> is <see langword="null"/>.</exception>
    public BlobStorageDataProtectionXmlRepository(IBlobStorage storage, ILoggerFactory? loggerFactory = null)
        : this(storage, containerManager: null, loggerFactory) { }

    /// <summary>Initializes a new instance that reads and writes data-protection key XML to the given blob storage.</summary>
    /// <param name="storage">The blob storage backend to read and write key XML files.</param>
    /// <param name="containerManager">Optional container manager used to ensure the DataProtection container before writing.</param>
    /// <param name="loggerFactory">Optional logger factory; when <see langword="null"/>, a no-op logger is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="storage"/> is <see langword="null"/>.</exception>
    public BlobStorageDataProtectionXmlRepository(
        IBlobStorage storage,
        IBlobContainerManager? containerManager,
        ILoggerFactory? loggerFactory = null
    )
    {
        Argument.IsNotNull(storage);
        _storage = storage;
        _containerManager = containerManager;
        _logger = loggerFactory?.CreateLogger(typeof(BlobStorageDataProtectionXmlRepository)) ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    /// <remarks>Sync-over-async: <see cref="IXmlRepository"/> is a synchronous interface; <see cref="Async.RunSync"/> bridges to the async implementation.</remarks>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        return Async.RunSync(_GetAllElementsAsync);
    }

    private async Task<IReadOnlyCollection<XElement>> _GetAllElementsAsync()
    {
        _logger.LogLoadingElements();

        var files = new List<BlobInfo>();
        await foreach (var blob in _storage.GetBlobsAsync(new BlobQuery(ContainerName), "*.xml").ConfigureAwait(false))
        {
            files.Add(blob);
        }

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
            await using var downloadResult = await _storage
                .OpenReadStreamAsync(new BlobLocation(ContainerName, file.BlobKey))
                .ConfigureAwait(false);

            if (downloadResult is null)
            {
                _logger.LogFailedToLoadElement(file.BlobKey);

                continue;
            }

            try
            {
                var element = await XElement
                    .LoadAsync(downloadResult.Stream, LoadOptions.None, CancellationToken.None)
                    .ConfigureAwait(false);
                elements.Add(element);
                _logger.LogLoadedElement(file.BlobKey);
            }
            catch (Exception ex) when (ex is XmlException or IOException)
            {
                _logger.LogMalformedElement(file.BlobKey, ex);
            }
        }

        return elements.AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sync-over-async: <see cref="IXmlRepository"/> is a synchronous interface; <see cref="Async.RunSync"/> bridges to the async implementation.
    /// The upload is retried up to 4 times on transient <see cref="IOException"/> or <see cref="HttpRequestException"/> failures.
    /// If all retries are exhausted, the underlying exception propagates.
    /// When <paramref name="friendlyName"/> is <see langword="null"/> or empty, a random GUID-based file name is used.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public void StoreElement(XElement element, string? friendlyName)
    {
        Argument.IsNotNull(element);
        var fileName = string.IsNullOrEmpty(friendlyName) ? $"{Guid.NewGuid():N}.xml" : $"{friendlyName}.xml";

        Async.RunSync(() => _StoreElementAsync(element, fileName));
    }

    private async Task _StoreElementAsync(XElement element, string fileName)
    {
        _logger.LogSavingElement(fileName);
        await _RetryPipeline.ExecuteAsync(storeElementAsync, (this, element, fileName)).ConfigureAwait(false);
        _logger.LogSavedElement(fileName);

        return;

        static async ValueTask storeElementAsync(
            (BlobStorageDataProtectionXmlRepository Repository, XElement Element, string FileName) state,
            CancellationToken cancellationToken
        )
        {
            var (repository, element, fileName) = state;

            await repository._EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

            await using var memoryStream = new MemoryStream();
            await element
                .SaveAsync(memoryStream, SaveOptions.DisableFormatting, cancellationToken)
                .ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);

            await repository
                ._storage.UploadAsync(
                    new BlobLocation(ContainerName, fileName),
                    memoryStream,
                    metadata: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    private async ValueTask _EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerManager is not null)
        {
            await _containerManager.EnsureContainerAsync(ContainerName, cancellationToken).ConfigureAwait(false);
        }
    }
}
