// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Api;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<AzuriteFixture>]
public sealed class BlobStorageDataProtectionIntegrationTests(AzuriteFixture fixture) : TestBase, IAsyncLifetime
{
    private BlobServiceClient? _blobServiceClient;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Clean up DataProtection container after tests to prevent blob accumulation
        if (_blobServiceClient is not null)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("dataprotection");
            if (await containerClient.ExistsAsync(AbortToken))
            {
                await containerClient.DeleteAsync(cancellationToken: AbortToken);
            }
        }
    }

    private AzureBlobStorage _CreateStorage()
    {
        _blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var azureStorageOptions = new AzureStorageOptions { CreateContainerIfNotExists = true };
        var optionsAccessor = new OptionsWrapper<AzureStorageOptions>(azureStorageOptions);
        var mimeTypeProvider = new MimeTypeProvider();
        var clock = new Clock(TimeProvider.System);
        var normalizer = new AzureBlobNamingNormalizer();

        return new AzureBlobStorage(
            _blobServiceClient,
            mimeTypeProvider,
            clock,
            optionsAccessor,
            normalizer,
            LoggerFactory.CreateLogger<AzureBlobStorage>()
        );
    }

    [Fact]
    public async Task should_persist_and_retrieve_keys_with_real_storage()
    {
        // Arrange
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, LoggerFactory);

        var keyId = Faker.Random.Guid().ToString("N");
        var element = new XElement(
            "key",
            new XAttribute("id", keyId),
            new XElement("creationDate", DateTimeOffset.UtcNow.ToString("O")),
            new XElement("encryptedKey", Faker.Random.AlphaNumeric(64))
        );

        // Act
        repository.StoreElement(element, $"key-{keyId}");
        var retrievedElements = repository.GetAllElements();

        // Assert
        retrievedElements.Should().NotBeEmpty();
        var retrieved = retrievedElements.FirstOrDefault(e => e.Attribute("id")?.Value == keyId);
        retrieved.Should().NotBeNull();
        retrieved!.ToString().Should().Be(element.ToString());
    }

    [Fact]
    public async Task should_persist_multiple_keys()
    {
        // Arrange
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, LoggerFactory);

        var keys = Enumerable
            .Range(0, 3)
            .Select(_ =>
            {
                var id = Faker.Random.Guid().ToString("N");
                return new XElement(
                    "key",
                    new XAttribute("id", id),
                    new XElement("creationDate", DateTimeOffset.UtcNow.ToString("O")),
                    new XElement("data", Faker.Random.AlphaNumeric(32))
                );
            })
            .ToList();

        // Act
        foreach (var key in keys)
        {
            repository.StoreElement(key, $"multi-key-{key.Attribute("id")!.Value}");
        }

        var retrievedElements = repository.GetAllElements();

        // Assert
        foreach (var key in keys)
        {
            var keyId = key.Attribute("id")!.Value;
            var retrieved = retrievedElements.FirstOrDefault(e => e.Attribute("id")?.Value == keyId);
            retrieved.Should().NotBeNull($"key {keyId} should be retrieved");
            retrieved!.ToString().Should().Be(key.ToString());
        }
    }

    [Fact]
    public async Task should_preserve_xml_structure()
    {
        // Arrange
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, LoggerFactory);

        var keyId = Faker.Random.Guid().ToString("N");
        var complexElement = new XElement(
            "key",
            new XAttribute("id", keyId),
            new XAttribute("version", "1"),
            new XElement(
                "descriptor",
                new XAttribute("type", "test-descriptor"),
                new XElement(
                    "keyEncryptor",
                    new XAttribute("name", "DPAPI-NG"),
                    new XElement("protectionDescriptor", "SID=S-1-5-21")
                ),
                new XElement(
                    "keyDerivation",
                    new XAttribute("algorithm", "SP800_108_CTR_HMACSHA512"),
                    new XElement("label", "DP Label"),
                    new XElement("context", "DP Context")
                )
            ),
            new XElement("creationDate", DateTimeOffset.UtcNow.ToString("O")),
            new XElement("activationDate", DateTimeOffset.UtcNow.AddDays(-1).ToString("O")),
            new XElement("expirationDate", DateTimeOffset.UtcNow.AddDays(90).ToString("O")),
            new XElement("masterKey", Convert.ToBase64String(Faker.Random.Bytes(32)))
        );

        // Act
        repository.StoreElement(complexElement, $"complex-key-{keyId}");
        var retrievedElements = repository.GetAllElements();

        // Assert
        var retrieved = retrievedElements.FirstOrDefault(e => e.Attribute("id")?.Value == keyId);
        retrieved.Should().NotBeNull();

        // Verify structure preserved
        retrieved!.Attribute("version")?.Value.Should().Be("1");
        retrieved.Element("descriptor").Should().NotBeNull();
        retrieved.Element("descriptor")!.Attribute("type")?.Value.Should().Be("test-descriptor");
        retrieved.Element("descriptor")!.Element("keyEncryptor").Should().NotBeNull();
        retrieved
            .Element("descriptor")!
            .Element("keyEncryptor")!
            .Element("protectionDescriptor")!
            .Value.Should()
            .Be("SID=S-1-5-21");
        retrieved.Element("descriptor")!.Element("keyDerivation")!.Element("label")!.Value.Should().Be("DP Label");
    }

    [Fact]
    public async Task should_handle_concurrent_operations()
    {
        // Arrange
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, LoggerFactory);
        const int keyCount = 10;

        var keys = Enumerable
            .Range(0, keyCount)
            .Select(i =>
            {
                var id = Faker.Random.Guid().ToString("N");
                return new XElement(
                    "key",
                    new XAttribute("id", id),
                    new XElement("index", i),
                    new XElement("data", Faker.Random.AlphaNumeric(32))
                );
            })
            .ToList();

        // Act - concurrent store operations
        Parallel.ForEach(
            keys,
            new ParallelOptions { MaxDegreeOfParallelism = 5 },
            key => repository.StoreElement(key, $"concurrent-key-{key.Attribute("id")!.Value}")
        );

        var retrievedElements = repository.GetAllElements();

        // Assert
        foreach (var key in keys)
        {
            var keyId = key.Attribute("id")!.Value;
            var retrieved = retrievedElements.FirstOrDefault(e => e.Attribute("id")?.Value == keyId);
            retrieved.Should().NotBeNull($"concurrent key {keyId} should be retrieved");
        }
    }
}
