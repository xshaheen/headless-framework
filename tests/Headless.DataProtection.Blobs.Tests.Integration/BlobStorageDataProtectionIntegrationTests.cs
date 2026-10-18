// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.DataProtection;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<AzuriteFixture>]
public sealed class BlobStorageDataProtectionIntegrationTests(AzuriteFixture fixture) : TestBase
{
    private BlobServiceClient? _blobServiceClient;

    protected override async ValueTask DisposeAsyncCore()
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

        await base.DisposeAsyncCore();
    }

    private AzureBlobStorage _CreateStorage()
    {
        _blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var azureStorageOptions = new AzureStorageOptions();
        var optionsAccessor = new OptionsWrapper<AzureStorageOptions>(azureStorageOptions);
        var mimeTypeProvider = new MimeTypeProvider();
        var clock = TimeProvider.System;
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

    private IBlobContainerManager _CreateContainerManager()
    {
        return new TestAzureBlobContainerManager(
            _blobServiceClient ?? throw new InvalidOperationException("Create storage before creating the manager."),
            new AzureBlobNamingNormalizer()
        );
    }

    [Fact]
    public async Task should_persist_multiple_keys()
    {
        // given
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, _CreateContainerManager(), LoggerFactory);

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

        // when
        foreach (var key in keys)
        {
            repository.StoreElement(key, $"multi-key-{key.Attribute("id")!.Value}");
        }

        var retrievedElements = repository.GetAllElements();

        // then
        foreach (var key in keys)
        {
            var keyId = key.Attribute("id")!.Value;
            var retrieved = retrievedElements.FirstOrDefault(e =>
                string.Equals(e.Attribute("id")?.Value, keyId, StringComparison.Ordinal)
            );
            retrieved.Should().NotBeNull($"key {keyId} should be retrieved");
            retrieved!.ToString().Should().Be(key.ToString());
        }
    }

    [Fact]
    public async Task should_preserve_xml_structure()
    {
        // given: the manager is required — the Azure data plane treats a missing container as an error
        // (no auto-create), so a fresh Azurite instance needs the ensure-before-write path.
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, _CreateContainerManager(), LoggerFactory);

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

        // when
        repository.StoreElement(complexElement, $"complex-key-{keyId}");
        var retrievedElements = repository.GetAllElements();

        // then
        var retrieved = retrievedElements.FirstOrDefault(e =>
            string.Equals(e.Attribute("id")?.Value, keyId, StringComparison.Ordinal)
        );
        retrieved.Should().NotBeNull();

        // Verify structure preserved
        retrieved!.Attribute("version")!.Value.Should().Be("1");
        retrieved.Element("descriptor").Should().NotBeNull();
        retrieved.Element("descriptor")!.Attribute("type")!.Value.Should().Be("test-descriptor");
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
        // given: the manager is required — the Azure data plane treats a missing container as an error
        // (no auto-create), so a fresh Azurite instance needs the ensure-before-write path.
        await using var storage = _CreateStorage();
        var repository = new BlobStorageDataProtectionXmlRepository(storage, _CreateContainerManager(), LoggerFactory);
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

        // when - concurrent store operations
        Parallel.ForEach(
            keys,
            new ParallelOptions { MaxDegreeOfParallelism = 5 },
            key => repository.StoreElement(key, $"concurrent-key-{key.Attribute("id")!.Value}")
        );

        var retrievedElements = repository.GetAllElements();

        // then
        foreach (var key in keys)
        {
            var keyId = key.Attribute("id")!.Value;
            var retrieved = retrievedElements.FirstOrDefault(e =>
                string.Equals(e.Attribute("id")?.Value, keyId, StringComparison.Ordinal)
            );
            retrieved.Should().NotBeNull($"concurrent key {keyId} should be retrieved");
        }
    }

    [Fact]
    public async Task should_keep_the_host_unready_until_the_key_container_exists_then_become_ready()
    {
        // given: the container is acknowledged as pre-provisioned but does not exist yet, so every key write fails
        // against the real backend until someone creates it.
        await using var storage = _CreateStorage();
        var container = _blobServiceClient!.GetBlobContainerClient("dataprotection");
        await container.DeleteIfExistsAsync(cancellationToken: AbortToken);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(LoggerProvider);
        builder
            .Services.AddDataProtection()
            .PersistKeysToBlobStorage(storage, provisioning: BlobContainerProvisioning.PreProvisioned)
            .ValidateKeyRing();
        using var host = builder.Build();

        // when: the start does not wait for the key store.
        await host.StartAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        var failing = await _WaitForReadinessAsync(
            host.Services,
            e => e.Exception?.Message.Contains("last error", StringComparison.Ordinal) == true
        );

        // then: unready, naming the validation and the missing container.
        failing.Status.Should().Be(HealthStatus.Unhealthy);
        failing.Exception!.Message.Should().Contain("DataProtectionKeyRing");
        failing.Exception.Message.Should().Contain("key-ring validation failed");

        // when: the container is provisioned, the retried validation writes a real key.
        await container.CreateIfNotExistsAsync(cancellationToken: AbortToken);
        var ready = await _WaitForReadinessAsync(host.Services, e => e.Status == HealthStatus.Healthy);

        // then
        ready.Status.Should().Be(HealthStatus.Healthy);
        var blobs = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: AbortToken))
        {
            blobs.Add(blob.Name);
        }

        blobs.Should().ContainSingle(name => name.StartsWith("key-", StringComparison.Ordinal));
        await host.StopAsync(AbortToken);
    }

    private static async Task<HealthReportEntry> _WaitForReadinessAsync(
        IServiceProvider services,
        Func<HealthReportEntry, bool> condition
    )
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        var health = services.GetRequiredService<HealthCheckService>();

        while (true)
        {
            var report = await health.CheckHealthAsync(
                r =>
                    string.Equals(
                        r.Name,
                        HeadlessWarmupServiceCollectionExtensions.HealthCheckName,
                        StringComparison.Ordinal
                    ),
                AbortToken
            );
            var entry = report.Entries[HeadlessWarmupServiceCollectionExtensions.HealthCheckName];

            if (condition(entry))
            {
                return entry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), limit.Token);
        }
    }

    private sealed class TestAzureBlobContainerManager(
        BlobServiceClient blobServiceClient,
        IBlobNamingNormalizer normalizer
    ) : IBlobContainerManager
    {
        public async ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            await blobServiceClient
                .GetBlobContainerClient(normalizer.NormalizeContainerName(container))
                .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<bool> ContainerExistsAsync(
            string container,
            CancellationToken cancellationToken = default
        )
        {
            var response = await blobServiceClient
                .GetBlobContainerClient(normalizer.NormalizeContainerName(container))
                .ExistsAsync(cancellationToken)
                .ConfigureAwait(false);

            return response.Value;
        }

        public async ValueTask<bool> DeleteContainerAsync(
            string container,
            CancellationToken cancellationToken = default
        )
        {
            var response = await blobServiceClient
                .GetBlobContainerClient(normalizer.NormalizeContainerName(container))
                .DeleteIfExistsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Value;
        }
    }
}
