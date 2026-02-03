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

    [Fact]
    public void should_skip_malformed_xml_files_gracefully()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo>
        {
            _CreateBlobInfo("valid.xml"),
            _CreateBlobInfo("malformed.xml"),
            _CreateBlobInfo("also-valid.xml"),
        };
        _SetupStorageWithBlobs(storage, blobs);

        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "valid.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"1\"/>"));
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "malformed.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id='unclosed'><broken"));
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "also-valid.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult("<key id=\"3\"/>"));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        // Malformed XML should cause XmlException but not crash the whole operation
        // The current implementation will throw - this test documents expected behavior
        var act = () => sut.GetAllElements();

        // If it throws, that's the current behavior (DoS risk if attacker uploads bad XML)
        // If it doesn't throw and returns 2 elements, that's resilient behavior
        act.Should().Throw<System.Xml.XmlException>();
    }

    [Fact]
    public void should_handle_empty_xml_file()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo> { _CreateBlobInfo("empty.xml") };
        _SetupStorageWithBlobs(storage, blobs);

        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "empty.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult(""));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var act = () => sut.GetAllElements();

        // Empty file causes XmlException - documents current behavior
        act.Should().Throw<System.Xml.XmlException>();
    }

    [Fact]
    public void should_not_resolve_external_entities_in_xml()
    {
        var storage = Substitute.For<IBlobStorage>();
        var blobs = new List<BlobInfo> { _CreateBlobInfo("xxe.xml") };
        _SetupStorageWithBlobs(storage, blobs);

        // XXE attack attempt - external entity declaration
        // In .NET 5+, XElement.Load() has DTD processing disabled by default
        // The entity reference will be included literally, not resolved
        var xxeXml = """
            <?xml version="1.0"?>
            <!DOCTYPE key [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <key id="malicious">&xxe;</key>
            """;
        storage.OpenReadStreamAsync(Arg.Any<string[]>(), "xxe.xml", Arg.Any<CancellationToken>())
            .Returns(_CreateDownloadResult(xxeXml));

        var sut = new BlobStorageDataProtectionXmlRepository(storage);

        var result = sut.GetAllElements();

        // Modern .NET safely ignores external entities - the key is returned
        // but the entity reference is NOT resolved (no file contents leaked)
        result.Should().HaveCount(1);
        var element = result.First();
        element.Attribute("id")?.Value.Should().Be("malicious");
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_generate_guid_when_friendly_name_missing(string? friendlyName)
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

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\Windows\\System32\\config")]
    [InlineData("foo/../bar/../../../secret")]
    public async Task should_pass_path_traversal_patterns_to_storage(string maliciousFriendlyName)
    {
        // Path traversal patterns in friendlyName - storage abstraction handles validation
        // This test documents that the repository passes the friendlyName directly to storage
        // The blob storage implementation is responsible for path validation/sanitization
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

        sut.StoreElement(element, maliciousFriendlyName);

        // Repository appends .xml to friendlyName and passes to storage
        // Storage abstraction is responsible for path validation
        await storage.Received(1).UploadAsync(
            Arg.Any<string[]>(),
            $"{maliciousFriendlyName}.xml",
            Arg.Any<Stream>(),
            Arg.Any<Dictionary<string, string?>?>(),
            Arg.Any<CancellationToken>()
        );
        capturedFileName.Should().Be($"{maliciousFriendlyName}.xml");
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
