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

namespace Headless.Api.DataProtection;

/// <summary>An <see cref="IXmlRepository"/> implementation that persists ASP.NET Core Data Protection keys as XML blobs in an <see cref="IBlobStorage"/> backend.</summary>
/// <remarks>
/// <para>
/// Keys are stored under a <c>DataProtection</c> container as individual <c>*.xml</c> files.
/// On <see cref="GetAllElements"/>, every blob in that container is enumerated except the reserved
/// <see cref="WriteProbeBlobName"/> sentinel; blobs that cannot be
/// downloaded (e.g. deleted between the listing and the read) are silently skipped.
/// Blobs whose content is not well-formed XML (<see cref="System.Xml.XmlException"/>) or cannot be
/// read due to I/O failures are also skipped and logged at <c>Warning</c> level — they do not abort
/// the load of the remaining keys. Loading is bounded to 1 MiB of actual streamed bytes per XML blob,
/// 1,000 XML blobs, and 16 MiB of aggregate XML; exceeding any bound aborts the entire key-ring load.
/// XML is parsed with DTD processing prohibited.
/// </para>
/// <para>
/// <see cref="StoreElement"/> retries transient I/O and HTTP failures up to 4 times with exponential
/// back-off and jitter. The container ensure (when an <see cref="IBlobContainerManager"/> is wired) runs inside the
/// same resilience pipeline as the upload, so transient ensure failures are retried under the same predicate.
/// When the write terminally fails (retries exhausted or a non-retried exception), the failure is wrapped in an
/// <see cref="InvalidOperationException"/> naming the <c>DataProtection</c> container, whether the ensure ran, and
/// the remediation — with the original exception preserved as the inner exception.
/// </para>
/// <para>Instances of this type are thread-safe.</para>
/// </remarks>
internal sealed class BlobStorageDataProtectionXmlRepository : IXmlRepository
{
    /// <summary>The top-level container all data-protection key XML blobs live in.</summary>
    internal const string ContainerName = "DataProtection";

    /// <summary>
    /// Reserved name of the sentinel blob written by <see cref="ProbeWriteAccessAsync"/>. Never part of the key
    /// ring: <see cref="GetAllElements"/> explicitly filters it out, so a crash between the probe's upload and
    /// delete can neither corrupt nor warn key-ring loading.
    /// </summary>
    internal const string WriteProbeBlobName = "startup-write-probe.xml";

    internal const int MaxXmlBlobSizeBytes = 1024 * 1024;
    internal const int MaxXmlElementCount = 1000;
    internal const int MaxAggregateXmlBytes = 16 * 1024 * 1024;

    private const int _ReadBufferSize = 81920;

    private readonly IBlobStorage _storage;
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
        ContainerManager = containerManager;
        _logger = loggerFactory?.CreateLogger(typeof(BlobStorageDataProtectionXmlRepository)) ?? NullLogger.Instance;
    }

    /// <summary>
    /// The container manager wired for ensure/existence probing, or <see langword="null"/> in pre-provisioned mode.
    /// Exposed so the key-ring health check can pivot between the cheap container-existence probe (manager wired)
    /// and the sentinel write probe (no manager).
    /// </summary>
    internal IBlobContainerManager? ContainerManager { get; }

    /// <inheritdoc />
    /// <remarks>Sync-over-async: <see cref="IXmlRepository"/> is a synchronous interface; <see cref="AsyncHelper.RunSync"/> bridges to the async implementation.</remarks>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        return AsyncHelper.RunSync(_GetAllElementsAsync);
    }

    private async Task<IReadOnlyCollection<XElement>> _GetAllElementsAsync()
    {
        _logger.LogLoadingElements();

        var elements = new List<XElement>();
        var readBuffer = new byte[_ReadBufferSize];
        var xmlBlobCount = 0;
        long aggregateBytesRead = 0;

        await foreach (var blob in _storage.GetBlobsAsync(new BlobQuery(ContainerName), "*.xml").ConfigureAwait(false))
        {
            // The startup write probe's sentinel is reserved and never part of the key ring; if a crash left one
            // behind (upload succeeded, delete did not), skip it so it cannot corrupt or warn key-ring loading.
            if (string.Equals(blob.BlobKey, WriteProbeBlobName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            xmlBlobCount++;
            if (xmlBlobCount > MaxXmlElementCount)
            {
                throw new InvalidOperationException(
                    $"Data Protection key-ring loading exceeded the {MaxXmlElementCount:N0} XML blob limit."
                );
            }

            _logger.LogLoadingElement(blob.BlobKey);
            await using var downloadResult = await _storage
                .OpenReadStreamAsync(new BlobLocation(ContainerName, blob.BlobKey))
                .ConfigureAwait(false);

            if (downloadResult is null)
            {
                _logger.LogFailedToLoadElement(blob.BlobKey);

                continue;
            }

            await using var xmlBuffer = new MemoryStream();

            try
            {
                long blobBytesRead = 0;

                while (true)
                {
                    var remainingBlobBytes = MaxXmlBlobSizeBytes - blobBytesRead;
                    var remainingAggregateBytes = MaxAggregateXmlBytes - aggregateBytesRead;
                    var remainingBytes = Math.Min(remainingBlobBytes, remainingAggregateBytes);
                    var readLength = (int)Math.Min(readBuffer.Length, remainingBytes + 1);
                    var bytesRead = await downloadResult
                        .Stream.ReadAsync(readBuffer.AsMemory(0, readLength), CancellationToken.None)
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    blobBytesRead += bytesRead;
                    if (blobBytesRead > MaxXmlBlobSizeBytes)
                    {
                        throw new InvalidOperationException(
                            $"Data Protection XML blob '{blob.BlobKey}' exceeded the {MaxXmlBlobSizeBytes:N0}-byte limit."
                        );
                    }

                    aggregateBytesRead += bytesRead;
                    if (aggregateBytesRead > MaxAggregateXmlBytes)
                    {
                        throw new InvalidOperationException(
                            $"Data Protection key-ring loading exceeded the {MaxAggregateXmlBytes:N0}-byte aggregate XML limit."
                        );
                    }

                    await xmlBuffer
                        .WriteAsync(readBuffer.AsMemory(0, bytesRead), CancellationToken.None)
                        .ConfigureAwait(false);
                }

                xmlBuffer.Position = 0;
                using var xmlReader = XmlReader.Create(
                    xmlBuffer,
                    new XmlReaderSettings
                    {
                        Async = true,
                        CloseInput = false,
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                    }
                );
                var element = await XElement
                    .LoadAsync(xmlReader, LoadOptions.None, CancellationToken.None)
                    .ConfigureAwait(false);
                elements.Add(element);
                _logger.LogLoadedElement(blob.BlobKey);
            }
            catch (Exception ex) when (ex is XmlException or IOException)
            {
                _logger.LogMalformedElement(blob.BlobKey, ex);
            }
        }

        if (xmlBlobCount == 0)
        {
            _logger.LogNoElementsFound();

            return [];
        }

        _logger.LogFoundElements(xmlBlobCount);

        return elements.AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sync-over-async: <see cref="IXmlRepository"/> is a synchronous interface; <see cref="AsyncHelper.RunSync"/> bridges to the async implementation.
    /// The container ensure (when a manager is wired) and the upload run inside one resilience pipeline that retries
    /// up to 4 times on transient <see cref="IOException"/> or <see cref="HttpRequestException"/> failures.
    /// A terminal failure (retries exhausted or a non-retried exception) is wrapped in an
    /// <see cref="InvalidOperationException"/> carrying the container name, whether the ensure ran, and the
    /// remediation, with the original exception as the inner exception.
    /// When <paramref name="friendlyName"/> is <see langword="null"/> or empty, a random GUID-based file name is used.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The write terminally failed; the original backend exception is the inner exception.</exception>
    public void StoreElement(XElement element, string? friendlyName)
    {
        Argument.IsNotNull(element);
        var fileName = string.IsNullOrEmpty(friendlyName) ? $"{Guid.NewGuid():N}.xml" : $"{friendlyName}.xml";

        // Construct (and thereby validate) the blob location before entering the resilience pipeline so invalid
        // friendly names (e.g. path traversal) surface as ArgumentException at the argument boundary instead of
        // being wrapped as a terminal storage failure below.
        var location = new BlobLocation(ContainerName, fileName);

        AsyncHelper.RunSync(() => _StoreElementAsync(element, location));
    }

    private async Task _StoreElementAsync(XElement element, BlobLocation location)
    {
        _logger.LogSavingElement(location.Path);

        try
        {
            await _RetryPipeline.ExecuteAsync(storeElementAsync, (this, element, location)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Context boundary, not classification: ANY terminal backend failure (provider exception types this package deliberately does not reference) is re-thrown wrapped with the container/manager context; nothing is swallowed and the original exception is preserved as the inner exception. Cancellation is excluded by the filter so it propagates untouched.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(_BuildTerminalStoreFailureMessage(location.Path), exception);
        }

        _logger.LogSavedElement(location.Path);

        return;

        static async ValueTask storeElementAsync(
            (BlobStorageDataProtectionXmlRepository Repository, XElement Element, BlobLocation Location) state,
            CancellationToken cancellationToken
        )
        {
            var (repository, element, location) = state;

            // The ensure runs INSIDE the resilience pipeline so the same transient predicate that protects the
            // upload also protects the container-ensure backend call.
            await repository._EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

            await using var memoryStream = new MemoryStream();
            await element
                .SaveAsync(memoryStream, SaveOptions.DisableFormatting, cancellationToken)
                .ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);

            await repository
                ._storage.UploadAsync(location, memoryStream, metadata: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies write access to the <c>DataProtection</c> container by uploading and deleting a reserved sentinel
    /// blob (<see cref="WriteProbeBlobName"/>) through the exact ensure + resilience pipeline the key writes use.
    /// This is how startup validation exercises the write path even when the key ring already holds a valid key —
    /// a state in which the protect/unprotect round-trip performs no write and lost write permission would
    /// otherwise stay hidden until the ~90-day key rotation.
    /// </summary>
    /// <exception cref="InvalidOperationException">The probe write terminally failed; the original backend exception is the inner exception.</exception>
    internal async Task ProbeWriteAccessAsync(CancellationToken cancellationToken = default)
    {
        var location = new BlobLocation(ContainerName, WriteProbeBlobName);

        _logger.LogSavingElement(location.Path);

        try
        {
            await _RetryPipeline.ExecuteAsync(probeAsync, (this, location), cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Context boundary, not classification: same rationale as _StoreElementAsync — the terminal backend failure is re-thrown wrapped with the container/manager context, original preserved as inner, cancellation excluded by the filter.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(_BuildTerminalStoreFailureMessage(location.Path), exception);
        }

        _logger.LogSavedElement(location.Path);

        return;

        static async ValueTask probeAsync(
            (BlobStorageDataProtectionXmlRepository Repository, BlobLocation Location) state,
            CancellationToken cancellationToken
        )
        {
            var (repository, location) = state;

            // Same shape as the key write: ensure INSIDE the pipeline, then upload — followed by a delete so the
            // sentinel does not accumulate. A retry re-running the upload after a transient delete failure is
            // harmless because the sentinel name is fixed and overwritten idempotently.
            await repository._EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

            await using var memoryStream = new MemoryStream("<startupWriteProbe />"u8.ToArray());
            await repository
                ._storage.UploadAsync(location, memoryStream, metadata: null, cancellationToken)
                .ConfigureAwait(false);

            _ = await repository._storage.DeleteAsync(location, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the terminal-failure message: context, not classification. It names the container, states whether a
    /// manager was wired (ensure ran) or not (pre-provisioned mode), and points at the remediation — it deliberately
    /// does NOT sniff the backend exception to guess a failure kind (e.g. "container missing").
    /// </summary>
    private string _BuildTerminalStoreFailureMessage(string fileName)
    {
        var ensureContext = ContainerManager is not null
            ? "An IBlobContainerManager was wired, so the container ensure ran inside the same retry pipeline as the write."
            : "No IBlobContainerManager is wired (pre-provisioned mode), so the container was not ensured before the write.";

        return $"Failed to persist data-protection key '{fileName}' to the '{ContainerName}' blob container. "
            + $"{ensureContext} Verify the '{ContainerName}' container exists and the credentials allow writes, wire "
            + "an IBlobContainerManager (the storage+manager overload of PersistKeysToBlobStorage, or a DI/keyed "
            + "overload that resolves one) so the container is ensured before writes, or see the "
            + "PersistKeysToBlobStorage provisioning documentation (BlobContainerProvisioning).";
    }

    private async ValueTask _EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (ContainerManager is not null)
        {
            await ContainerManager.EnsureContainerAsync(ContainerName, cancellationToken).ConfigureAwait(false);
        }
    }
}
