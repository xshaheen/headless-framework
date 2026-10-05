// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Blobs;
using Headless.Hosting;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

[Collection<AzureBlobStorageFixture>]
public sealed class AzureBlobsHealthCheckTests(AzureBlobStorageFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_default_and_named_azure_stores_healthy()
    {
        // given
        // Pinned to a service version the Azurite image accepts, as the storage tests do.
        var client = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.AddSingleton(client);
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseAzure(_ => { });
            blobs.AddNamed("archive", instance => instance.UseAzure(_ => { }, clientFactory: _ => client));
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        report.Entries["blobs-azure"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["blobs-azure-archive"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["blobs-azure"].Tags.Should().Contain(HeadlessHealthCheckTags.Blobs);
    }
}
