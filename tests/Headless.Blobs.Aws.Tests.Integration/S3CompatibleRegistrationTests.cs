// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Registration-shape and options-validation tests for UseS3Compatible. No S3 I/O is performed, so no Docker is
// required: AmazonS3Client construction is lazy and every request is deferred to calls these tests never make.

using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class S3CompatibleRegistrationTests
{
    private const string _HttpsUrl = "https://minio.example.test:9000";

    private static ServiceCollection _BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();

        return services;
    }

    [Fact]
    public async Task should_register_storage_and_container_manager_when_default_store_uses_s3_compatible()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs => blobs.UseS3Compatible(_HttpsUrl, "access", "secret"));

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var storage = serviceProvider.GetRequiredService<IBlobStorage>();
        var manager = serviceProvider.GetService<IBlobContainerManager>();

        // then
        storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        manager.Should().NotBeNull();
    }

    [Fact]
    public async Task should_register_keyed_services_when_named_store_uses_s3_compatible()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs =>
            blobs.AddNamed("media", instance => instance.UseS3Compatible(_HttpsUrl, "access", "secret"))
        );

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var storage = serviceProvider.GetRequiredKeyedService<IBlobStorage>("media");
        var presigned = serviceProvider.GetRequiredKeyedService<IPresignedUrlBlobStorage>("media");
        var manager = serviceProvider.GetKeyedService<IBlobContainerManager>("media");

        // then
        presigned.Should().BeSameAs(storage);
        manager.Should().NotBeNull();
        serviceProvider.GetService<IBlobStorage>().Should().BeNull();
    }

    [Fact]
    public async Task should_reject_plaintext_http_when_allow_insecure_http_is_not_set()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs => blobs.UseS3Compatible("http://localhost:9000", "access", "secret"));

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var act = () => serviceProvider.GetRequiredService<IOptions<S3CompatibleBlobStorageOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*AllowInsecureHttp*");
    }

    [Fact]
    public async Task should_accept_plaintext_http_when_allow_insecure_http_is_set()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs =>
            blobs.UseS3Compatible(
                "http://localhost:9000",
                "access",
                "secret",
                options => options.AllowInsecureHttp = true
            )
        );

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var storage = serviceProvider.GetRequiredService<IBlobStorage>();

        // then
        storage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("minio:9000")]
    [InlineData("ftp://minio.example.test")]
    public async Task should_reject_service_url_when_it_is_not_an_absolute_http_or_https_url(string serviceUrl)
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs => blobs.UseS3Compatible(serviceUrl, "access", "secret"));

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var act = () => serviceProvider.GetRequiredService<IOptions<S3CompatibleBlobStorageOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*absolute http or https URL*");
    }

    [Fact]
    public async Task should_bind_options_when_registered_from_configuration_section()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Minio:ServiceUrl"] = "http://localhost:9000",
                    ["Minio:AccessKeyId"] = "access",
                    ["Minio:SecretAccessKey"] = "secret",
                    ["Minio:AuthenticationRegion"] = "eu-west-1",
                    ["Minio:AllowInsecureHttp"] = "true",
                    ["Minio:MaxBulkParallelism"] = "4",
                }
            )
            .Build();

        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs => blobs.UseS3Compatible(configuration.GetSection("Minio")));

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var options = serviceProvider.GetRequiredService<IOptions<S3CompatibleBlobStorageOptions>>().Value;

        // then
        options.ServiceUrl.Should().Be("http://localhost:9000");
        options.AccessKeyId.Should().Be("access");
        options.SecretAccessKey.Should().Be("secret");
        options.AuthenticationRegion.Should().Be("eu-west-1");
        options.AllowInsecureHttp.Should().BeTrue();
        options.MaxBulkParallelism.Should().Be(4);
        serviceProvider.GetRequiredService<IBlobStorage>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("", "access", "secret")]
    [InlineData(_HttpsUrl, " ", "secret")]
    [InlineData(_HttpsUrl, "access", "")]
    public void should_throw_when_a_positional_argument_is_blank(
        string serviceUrl,
        string accessKeyId,
        string secretAccessKey
    )
    {
        // given
        var services = _BuildBaseServices();

        // when
        var act = () =>
            services.AddHeadlessBlobs(blobs => blobs.UseS3Compatible(serviceUrl, accessKeyId, secretAccessKey));

        // then
        act.Should().Throw<ArgumentException>();
    }
}
