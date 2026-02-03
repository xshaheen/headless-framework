// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Api;
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
        var act = () => sut.GetAllElements();
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
        var blobs = new List<BlobInfo>
        {
            _CreateBlobInfo("key1.xml"),
            _CreateBlobInfo("key2.xml"),
            _CreateBlobInfo("key3.xml"),
        };
        _SetupStorageWithBlobs(storage, blobs);

        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key1.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"1\"/>"));
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key2.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"2\"/>"));
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key3.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"3\"/>"));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().HaveCount(3);
    }

    [Fact]
    public void should_skip_files_that_fail_to_download()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo>
        {
            _CreateBlobInfo("key1.xml"),
            _CreateBlobInfo("key2.xml"),
            _CreateBlobInfo("key3.xml"),
        };
        _SetupStorageWithBlobs(storage, blobs);

        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key1.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"1\"/>"));
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key2.xml", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<BlobDownloadResult?>(null)); // Download fails
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key3.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"3\"/>"));

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

        await storage.Received(1).GetPagedListAsync(
            Arg.Is<string[]>(c => c.Length == 1 && c[0] == "DataProtection"),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task should_filter_by_xml_extension()
    {
        var storage = Substitute.For<IBlobStorage>();
        _SetupEmptyStorage(storage);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        _ = sut.GetAllElements();

        await storage.Received(1).GetPagedListAsync(
            Arg.Any<string[]>(),
            "*.xml",
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public void should_parse_xml_content_correctly()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo> { _CreateBlobInfo("test-key.xml") };
        _SetupStorageWithBlobs(storage, blobs);

        var xmlContent = """
            <key id="test-123" version="1">
              <creationDate>2026-01-01T00:00:00Z</creationDate>
              <encryptedKey>base64data</encryptedKey>
            </key>
            """;
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "test-key.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult(xmlContent));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        result.Should().HaveCount(1);
        var element = result.First();
        element.Name.LocalName.Should().Be("key");
        element.Attribute("id")?.Value.Should().Be("test-123");
        element.Attribute("version")?.Value.Should().Be("1");
        element.Element("creationDate")?.Value.Should().Be("2026-01-01T00:00:00Z");
        element.Element("encryptedKey")?.Value.Should().Be("base64data");
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
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, "key-123");

        await storage.Received(1).UploadAsync(
            Arg.Any<string[]>(),
            "key-123.xml",
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public void should_generate_guid_when_no_friendly_name()
    {
        var storage = Substitute.For<IBlobStorage>();
        string? capturedFileName = null;
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Do<string>(x => capturedFileName = x),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, friendlyName: null);

        capturedFileName.Should().NotBeNull();
        capturedFileName.Should().EndWith(".xml");
        // Should be a valid GUID without dashes (32 chars) + ".xml" (4 chars)
        capturedFileName.Should().HaveLength(36);
        var guidPart = capturedFileName![..^4];
        Guid.TryParse(guidPart, out _).Should().BeTrue();
    }

    [Fact]
    public void should_generate_guid_when_empty_friendly_name()
    {
        var storage = Substitute.For<IBlobStorage>();
        string? capturedFileName = null;
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Do<string>(x => capturedFileName = x),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, friendlyName: "");

        capturedFileName.Should().NotBeNull();
        capturedFileName.Should().EndWith(".xml");
        capturedFileName.Should().HaveLength(36);
        var guidPart = capturedFileName![..^4];
        Guid.TryParse(guidPart, out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_upload_to_DataProtection_container()
    {
        var storage = Substitute.For<IBlobStorage>();
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);
        var element = new XElement("key", new XAttribute("id", "test"));

        sut.StoreElement(element, "test-key");

        await storage.Received(1).UploadAsync(
            Arg.Is<string[]>(c => c.Length == 1 && c[0] == "DataProtection"),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public void should_save_xml_without_formatting()
    {
        var storage = Substitute.For<IBlobStorage>();
        byte[]? capturedBytes = null;
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Do<Stream>(s =>
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                capturedBytes = ms.ToArray();
            }),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var element = new XElement("key",
            new XAttribute("id", "test"),
            new XElement("child", "value")
        );

        sut.StoreElement(element, "test-key");

        capturedBytes.Should().NotBeNull();
        var xmlString = System.Text.Encoding.UTF8.GetString(capturedBytes!);
        // DisableFormatting means no extra whitespace/newlines between elements
        xmlString.Should().NotContain("\n  ");
        xmlString.Should().Contain("<key id=\"test\"><child>value</child></key>");
    }

    [Fact]
    public void should_retry_on_IOException()
    {
        var storage = Substitute.For<IBlobStorage>();
        var callCount = 0;
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(_ =>
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

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task should_handle_concurrent_GetAllElements_calls()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo> { _CreateBlobInfo("key1.xml") };
        _SetupStorageWithBlobs(storage, blobs);
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "key1.xml", Arg.Any<CancellationToken>())
            .Returns(_ => _CreateDownloadResult("<key id=\"1\"/>"));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => sut.GetAllElements()))
            .ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            result.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task should_handle_concurrent_StoreElement_calls()
    {
        var storage = Substitute.For<IBlobStorage>();
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.CompletedTask);
        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
            {
                var element = new XElement("key", new XAttribute("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                sut.StoreElement(element, $"key-{i}");
            }))
            .ToList();

        await Task.WhenAll(tasks);

        // If we reach here without exception, the test passes
        await storage.ReceivedWithAnyArgs(10).UploadAsync(
            default!,
            default!,
            default!,
            default,
            default
        );
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    public void should_round_trip_xml_element()
    {
        // Use an in-memory dictionary to simulate blob storage
        var storedBlobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var storage = Substitute.For<IBlobStorage>();

        // Setup upload to store in dictionary
        storage.UploadAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        ).Returns(callInfo =>
        {
            var blobName = callInfo.ArgAt<string>(1);
            var stream = callInfo.ArgAt<Stream>(2);
            using var ms = new MemoryStream();
#pragma warning disable VSTHRD103, CA1849 // CopyTo in sync callback - acceptable for test
            stream.CopyTo(ms);
#pragma warning restore VSTHRD103, CA1849
            storedBlobs[blobName] = ms.ToArray();
            return ValueTask.CompletedTask;
        });

        // Setup list to return stored blobs
        storage.GetPagedListAsync(
            Arg.Any<string[]>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        ).Returns(_ =>
        {
            var blobs = storedBlobs.Keys
                .Where(k => k.EndsWith(".xml", StringComparison.Ordinal))
                .Select(_CreateBlobInfo)
                .ToList();
            return ValueTask.FromResult(new PagedFileListResult(blobs));
        });

        // Setup download to return stored content
        storage.OpenReadStreamAsync(
            Arg.Any<string[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()
        ).Returns(callInfo =>
        {
            var blobName = callInfo.ArgAt<string>(1);
            if (storedBlobs.TryGetValue(blobName, out var data))
            {
                return ValueTask.FromResult<BlobDownloadResult?>(
                    new BlobDownloadResult(new MemoryStream(data), blobName)
                );
            }

            return ValueTask.FromResult<BlobDownloadResult?>(null);
        });

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var originalElement = new XElement("key",
            new XAttribute("id", "round-trip-test"),
            new XAttribute("version", "1"),
            new XElement("creationDate", "2026-01-01T00:00:00Z"),
            new XElement("activationDate", "2026-01-01T00:00:00Z"),
            new XElement("expirationDate", "2026-04-01T00:00:00Z"),
            new XElement("encryptedKey", "SGVsbG8gV29ybGQ=")
        );

        sut.StoreElement(originalElement, "round-trip-key");

        var retrievedElements = sut.GetAllElements();

        retrievedElements.Should().HaveCount(1);
        var retrievedElement = retrievedElements.First();
        retrievedElement.Attribute("id")?.Value.Should().Be("round-trip-test");
        retrievedElement.Attribute("version")?.Value.Should().Be("1");
        retrievedElement.Element("creationDate")?.Value.Should().Be("2026-01-01T00:00:00Z");
        retrievedElement.Element("encryptedKey")?.Value.Should().Be("SGVsbG8gV29ybGQ=");
    }

    #endregion

    #region Helper Methods

    private static void _SetupEmptyStorage(IBlobStorage storage)
    {
        storage.GetPagedListAsync(
            Arg.Any<string[]>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.FromResult(PagedFileListResult.Empty));
    }

    private static void _SetupStorageWithBlobs(IBlobStorage storage, IReadOnlyCollection<BlobInfo> blobs)
    {
        storage.GetPagedListAsync(
            Arg.Any<string[]>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.FromResult(new PagedFileListResult(blobs)));
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
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xmlContent));
        return ValueTask.FromResult<BlobDownloadResult?>(new BlobDownloadResult(stream, "test.xml"));
    }

    #endregion
}
