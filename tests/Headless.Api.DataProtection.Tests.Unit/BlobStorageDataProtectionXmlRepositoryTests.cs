// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Api.DataProtection;
using Headless.Blobs;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class BlobStorageDataProtectionXmlRepositoryTests
{
    #region Constructor Tests

    [Fact]
    public void should_throw_when_storage_null()
    {
        var act = () => new BlobStorageDataProtectionXmlRepository(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("storage");
    }

    [Fact]
    public void should_use_null_logger_when_factory_null()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupEmptyStorage(storage);

        var sut = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory: null);

        // Should not throw when called without logger factory
        var act = sut.GetAllElements;
        act.Should().NotThrow();
    }

    [Fact]
    public void should_create_logger_from_factory()
    {
        var storage = Substitute.For<IBlobStorage>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        _ = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory);

        loggerFactory.Received(1).CreateLogger(typeof(BlobStorageDataProtectionXmlRepository).FullName!);
    }

    #endregion

    #region GetAllElements Tests

    [Fact]
    public void should_return_empty_when_no_files()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupEmptyStorage(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().BeEmpty();
    }

    [Fact]
    public void should_return_all_xml_elements()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(
            storage,
            [_CreateBlobInfo("key1.xml"), _CreateBlobInfo("key2.xml"), _CreateBlobInfo("key3.xml")]
        );

        _SetupDownload(storage, "key1.xml", "<key id=\"1\"/>");
        _SetupDownload(storage, "key2.xml", "<key id=\"2\"/>");
        _SetupDownload(storage, "key3.xml", "<key id=\"3\"/>");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().HaveCount(3);
    }

    [Fact]
    public void should_skip_files_that_fail_to_download()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(
            storage,
            [_CreateBlobInfo("key1.xml"), _CreateBlobInfo("key2.xml"), _CreateBlobInfo("key3.xml")]
        );

        _SetupDownload(storage, "key1.xml", "<key id=\"1\"/>");
        storage
            .OpenReadStreamAsync(Arg.Is<BlobLocation>(l => l.Path == "key2.xml"), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<BlobDownloadResult?>(null)); // Download fails
        _SetupDownload(storage, "key3.xml", "<key id=\"3\"/>");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().HaveCount(2);
        result.Select(x => x.Attribute("id")?.Value).Should().BeEquivalentTo(["1", "3"]);
    }

    [Fact]
    public async Task should_load_elements_from_DataProtection_container()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupEmptyStorage(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        _ = sut.GetAllElements();

        await storage
            .Received(1)
            .ListAsync(Arg.Is<BlobQuery>(q => q.Container == "DataProtection"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_filter_by_xml_extension()
    {
        // The *.xml filter is now a client-side glob over the listing; non-xml blobs are not loaded.
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(storage, [_CreateBlobInfo("key.xml"), _CreateBlobInfo("ignore.txt")]);
        _SetupDownload(storage, "key.xml", "<key id=\"1\"/>");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().ContainSingle();
        await storage
            .DidNotReceive()
            .OpenReadStreamAsync(Arg.Is<BlobLocation>(l => l.Path == "ignore.txt"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_parse_xml_content_correctly()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(storage, [_CreateBlobInfo("test-key.xml")]);

        const string xmlContent = """
            <key id="test-123" version="1">
              <creationDate>2026-01-01T00:00:00Z</creationDate>
              <encryptedKey>base64data</encryptedKey>
            </key>
            """;

        _SetupDownload(storage, "test-key.xml", xmlContent);

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().ContainSingle();
        var element = result.First();
        element.Name.LocalName.Should().Be("key");
        element.Attribute("id")!.Value.Should().Be("test-123");
        element.Attribute("version")!.Value.Should().Be("1");
        element.Element("creationDate")!.Value.Should().Be("2026-01-01T00:00:00Z");
        element.Element("encryptedKey")!.Value.Should().Be("base64data");
    }

    [Fact]
    public void should_skip_malformed_xml_files_gracefully()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(
            storage,
            [_CreateBlobInfo("valid.xml"), _CreateBlobInfo("malformed.xml"), _CreateBlobInfo("also-valid.xml")]
        );

        _SetupDownload(storage, "valid.xml", "<key id=\"1\"/>");
        _SetupDownload(storage, "malformed.xml", "<key id='unclosed'><broken");
        _SetupDownload(storage, "also-valid.xml", "<key id=\"3\"/>");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        // Malformed XML is caught per-blob; valid blobs are still returned
        var result = sut.GetAllElements();

        result.Should().HaveCount(2);
        result.Select(x => x.Attribute("id")?.Value).Should().BeEquivalentTo(["1", "3"]);
    }

    [Fact]
    public void should_handle_empty_xml_file()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(storage, [_CreateBlobInfo("empty.xml")]);
        _SetupDownload(storage, "empty.xml", "");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        // Empty file is malformed XML; the blob is skipped and an empty list is returned
        var result = sut.GetAllElements();

        result.Should().BeEmpty();
    }

    [Fact]
    public void should_not_resolve_external_entities_in_xml()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(storage, [_CreateBlobInfo("xxe.xml")]);

        // XXE attack attempt - external entity declaration
        // In .NET 5+, XElement.Load() has DTD processing disabled by default
        // The entity reference will be included literally, not resolved
        const string xxeXml = """
            <?xml version="1.0"?>
            <!DOCTYPE key [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <key id="malicious">&xxe;</key>
            """;

        _SetupDownload(storage, "xxe.xml", xxeXml);

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        // Modern .NET safely ignores external entities - the key is returned
        // but the entity reference is NOT resolved (no file contents leaked)
        result.Should().ContainSingle();
        var element = result.First();
        element.Attribute("id")?.Value.Should()?.Be("malicious");
        // The value should NOT contain /etc/passwd contents - DTD expansion is disabled
        element.Value.Should().NotContain("root:");
    }

    #endregion

    #region StoreElement Tests

    [Fact]
    public void should_throw_when_element_null()
    {
        var storage = Substitute.For<IBlobStorage>();
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var act = () => sut.StoreElement(null!, "test");

        act.Should().Throw<ArgumentNullException>().WithParameterName("element");
    }

    [Fact]
    public async Task should_use_friendly_name_for_filename()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupUpload(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, "key-123");

        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == "key-123.xml"),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_generate_guid_when_friendly_name_missing(string? friendlyName)
    {
        var storage = Substitute.For<IBlobStorage>();
        string? capturedFileName = null;
        storage
            .UploadAsync(
                Arg.Do<BlobLocation>(l => capturedFileName = l.Path),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, friendlyName);

        capturedFileName.Should().NotBeNull();
        capturedFileName.Should().EndWith(".xml");
        // Should be a valid GUID without dashes (32 chars) + ".xml" (4 chars)
        capturedFileName.Should().HaveLength(36);
        var guidPart = capturedFileName![..^4];
        Guid.TryParse(guidPart, out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_upload_to_DataProtection_container()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupUpload(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, "test-key");

        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Container == "DataProtection"),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_ensure_DataProtection_container_before_upload_when_manager_is_available()
    {
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        var calls = new List<string>();

        manager
            .EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("ensure");
                return ValueTask.CompletedTask;
            });

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                calls.Add("upload");
                return ValueTask.CompletedTask;
            });

        var sut = new BlobStorageDataProtectionXmlRepository(storage, manager);

        sut.StoreElement(new XElement("key", new XAttribute("id", "test")), "test-key");

        calls.Should().Equal("ensure", "upload");

        await manager.Received(1).EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_save_xml_without_formatting()
    {
        var storage = Substitute.For<IBlobStorage>();
        byte[]? capturedBytes = null;
        async ValueTask captureUploadAsync(Stream stream)
        {
            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, TestContext.Current.CancellationToken);
            capturedBytes = ms.ToArray();
        }

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => captureUploadAsync(callInfo.ArgAt<Stream>(1)));
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var element = new XElement("key", new XAttribute("id", "test"), new XElement("child", "value"));

        sut.StoreElement(element, "test-key");

        capturedBytes.Should().NotBeNull();
        var xmlString = Encoding.UTF8.GetString(capturedBytes!);
        // DisableFormatting means no extra whitespace/newlines between elements
        xmlString.Should().NotContain("\n  ");
        xmlString.Should().Contain("<key id=\"test\"><child>value</child></key>");
    }

    [Fact]
    public void should_retry_on_IOException()
    {
        var storage = Substitute.For<IBlobStorage>();
        var callCount = 0;
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new IOException("Simulated transient failure");
                }

                return ValueTask.CompletedTask;
            });
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, "test-key");

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task should_retry_when_container_ensure_throws_transient_exception()
    {
        // The container ensure runs INSIDE the same resilience pipeline as the upload, so a transient
        // ensure failure is retried under the same predicate instead of failing the key write outright.
        var storage = Substitute.For<IBlobStorage>();
        _SetupUpload(storage);
        var manager = Substitute.For<IBlobContainerManager>();
        var ensureCalls = 0;
        manager
            .EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ensureCalls++;
                if (ensureCalls == 1)
                {
                    throw new IOException("Simulated transient ensure failure");
                }

                return ValueTask.CompletedTask;
            });
        var sut = new BlobStorageDataProtectionXmlRepository(storage, manager);

        sut.StoreElement(new XElement("key", new XAttribute("id", "test")), "test-key");

        ensureCalls.Should().Be(2);
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_wrap_terminal_store_failure_with_container_context_when_no_manager()
    {
        // Retries exhausted on a transient-shaped failure → the surfaced exception adds the container name and
        // the no-manager (pre-provisioned) context, with the original backend exception preserved as the inner.
        var storage = Substitute.For<IBlobStorage>();
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new IOException("Simulated persistent failure"));
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        var act = () => sut.StoreElement(element, "test-key");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'DataProtection'*")
            .WithMessage("*No IBlobContainerManager*")
            .WithInnerException<IOException>();
    }

    [Fact]
    public async Task should_wrap_non_retried_store_failure_noting_manager_was_wired()
    {
        // A non-transient failure is not retried (the predicate only covers IOException/HttpRequestException)
        // but is still wrapped with the container + manager-was-wired context.
        var storage = Substitute.For<IBlobStorage>();
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated non-transient failure"));
        var manager = Substitute.For<IBlobContainerManager>();
        var sut = new BlobStorageDataProtectionXmlRepository(storage, manager);
        var element = new XElement("key", new XAttribute("id", "test"));

        var act = () => sut.StoreElement(element, "test-key");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'DataProtection'*")
            .WithMessage("*IBlobContainerManager was wired*")
            .WithInnerException<NotSupportedException>();
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\Windows\\System32\\config")]
    [InlineData("foo/../bar/../../../secret")]
    public void should_reject_path_traversal_friendly_names(string maliciousFriendlyName)
    {
        // Path traversal is now rejected at the BlobLocation boundary (the repository constructs a
        // BlobLocation, whose constructor validates the key) rather than being passed through to storage.
        var storage = Substitute.For<IBlobStorage>();
        _SetupUpload(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        var act = () => sut.StoreElement(element, maliciousFriendlyName);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ProbeWriteAccessAsync Tests

    [Fact]
    public async Task should_upload_then_delete_sentinel_when_probing_write_access()
    {
        // The write probe must exercise the exact write path the key writes use: ensure (when a manager is
        // wired) → upload the reserved sentinel → delete it.
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        var calls = new List<string>();

        manager
            .EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("ensure");
                return ValueTask.CompletedTask;
            });
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                calls.Add("upload");
                return ValueTask.CompletedTask;
            });
        storage
            .DeleteAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("delete");
                return ValueTask.FromResult(true);
            });

        var sut = new BlobStorageDataProtectionXmlRepository(storage, manager);

        await sut.ProbeWriteAccessAsync(TestContext.Current.CancellationToken);

        calls.Should().Equal("ensure", "upload", "delete");
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l =>
                    l.Container == "DataProtection"
                    && l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName
                ),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .DeleteAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_exclude_write_probe_sentinel_from_GetAllElements()
    {
        // A crash between the probe's upload and delete leaves the sentinel behind; the key-ring load must
        // neither read it nor warn about it.
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(
            storage,
            [_CreateBlobInfo("key1.xml"), _CreateBlobInfo(BlobStorageDataProtectionXmlRepository.WriteProbeBlobName)]
        );
        _SetupDownload(storage, "key1.xml", "<key id=\"1\"/>");

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().ContainSingle();
        result.First().Attribute("id")!.Value.Should().Be("1");
        await storage
            .DidNotReceive()
            .OpenReadStreamAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_wrap_terminal_write_probe_failure_with_container_context()
    {
        // The probe reuses the write path's terminal wrap: container named, manager context, original as inner.
        var storage = Substitute.For<IBlobStorage>();
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated lost write access"));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var act = async () => await sut.ProbeWriteAccessAsync(TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'DataProtection'*")
            .WithMessage("*No IBlobContainerManager*")
            .WithInnerException<InvalidOperationException, NotSupportedException>();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task should_handle_concurrent_GetAllElements_calls()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupStorageWithBlobs(storage, [_CreateBlobInfo("key1.xml")]);
        storage
            .OpenReadStreamAsync(Arg.Is<BlobLocation>(l => l.Path == "key1.xml"), Arg.Any<CancellationToken>())
            .Returns(_ => _CreateDownloadResult("<key id=\"1\"/>"));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(sut.GetAllElements)).ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            result.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task should_handle_concurrent_StoreElement_calls()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupUpload(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var tasks = Enumerable
            .Range(0, 10)
            .Select(i =>
                Task.Run(() =>
                {
                    var element = new XElement("key", new XAttribute("id", i.ToString(CultureInfo.InvariantCulture)));
                    sut.StoreElement(element, $"key-{i}");
                })
            )
            .ToList();

        await Task.WhenAll(tasks);

        // If we reach here without exception, the test passes
        await storage.ReceivedWithAnyArgs(10).UploadAsync(default, null!, null, CancellationToken.None);
    }

    #endregion

    #region Helper Methods

    private static void _SetupEmptyStorage(IBlobStorage storage)
    {
        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(BlobPage.Empty));
    }

    private static void _SetupStorageWithBlobs(IBlobStorage storage, IReadOnlyList<BlobInfo> blobs)
    {
        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new BlobPage(blobs, null)));
    }

    private static void _SetupUpload(IBlobStorage storage)
    {
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);
    }

    private static void _SetupDownload(IBlobStorage storage, string path, string xmlContent)
    {
        storage
            .OpenReadStreamAsync(Arg.Is<BlobLocation>(l => l.Path == path), Arg.Any<CancellationToken>())
            .Returns(_ => _CreateDownloadResult(xmlContent));
    }

    private static BlobInfo _CreateBlobInfo(string blobKey)
    {
        return new BlobInfo
        {
            BlobKey = blobKey,
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Size = 100,
        };
    }

    private static ValueTask<BlobDownloadResult?> _CreateDownloadResult(string xmlContent)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlContent));

#pragma warning disable CA2000 // Dispose objects before losing scope
        var blobDownloadResult = new BlobDownloadResult(stream, "test.xml");
#pragma warning restore CA2000

        return ValueTask.FromResult<BlobDownloadResult?>(blobDownloadResult);
    }

    #endregion
}
