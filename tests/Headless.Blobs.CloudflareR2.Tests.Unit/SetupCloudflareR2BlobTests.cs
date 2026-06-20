// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Blobs.CloudflareR2;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupCloudflareR2BlobTests : TestBase
{
    private static ServiceProvider _BuildProvider(Action<R2BlobStorageOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClock>(new Clock(TimeProvider.System));
        services.AddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.AddLogging();

        services.AddCloudflareR2BlobStorage(
            configure
                ?? (
                    options =>
                    {
                        options.AccountId = "acc123";
                        options.AccessKeyId = "key";
                        options.SecretAccessKey = "secret";
                    }
                )
        );

        return services.BuildServiceProvider();
    }

    [Fact]
    public void registers_amazon_s3_configured_for_r2()
    {
        using var sp = _BuildProvider();

        var config = (AmazonS3Config)((AmazonS3Client)sp.GetRequiredService<IAmazonS3>()).Config;

        // The AWS SDK normalizes ServiceURL with a trailing slash.
        config.ServiceURL.Should().Be("https://acc123.r2.cloudflarestorage.com/");
        config.ForcePathStyle.Should().BeTrue();
        config.AuthenticationRegion.Should().Be("auto");
        config.RequestChecksumCalculation.Should().Be(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.Should().Be(ResponseChecksumValidation.WHEN_REQUIRED);
    }

    [Fact]
    public void applies_r2_safe_blob_storage_options()
    {
        using var sp = _BuildProvider();

        var options = sp.GetRequiredService<IOptions<AwsBlobStorageOptions>>().Value;

        options.CannedAcl.Should().BeNull();
        options.UseChunkEncoding.Should().BeFalse();
        options.DisablePayloadSigning.Should().BeTrue();
        options.AutoCreateContainer.Should().BeFalse();
    }

    [Fact]
    public void registers_r2_naming_normalizer()
    {
        using var sp = _BuildProvider();

        sp.GetRequiredService<IBlobNamingNormalizer>().Should().BeOfType<R2BlobNamingNormalizer>();
    }

    [Fact]
    public async Task registers_aws_engine_with_presigned_capability()
    {
        // AwsBlobStorage is IAsyncDisposable, so the provider must be disposed asynchronously once resolved.
        await using var sp = _BuildProvider();

        var storage = sp.GetRequiredService<IBlobStorage>();

        storage.Should().BeOfType<AwsBlobStorage>();
        storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
    }

    [Fact]
    public async Task presigned_capability_is_injectable_and_is_the_same_instance()
    {
        await using var sp = _BuildProvider();

        var presigned = sp.GetRequiredService<IPresignedUrlBlobStorage>();

        presigned.Should().BeSameAs(sp.GetRequiredService<IBlobStorage>());
    }

    [Fact]
    public void binds_options_from_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["R2:AccountId"] = "acc123",
                    ["R2:AccessKeyId"] = "key",
                    ["R2:SecretAccessKey"] = "secret",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new Clock(TimeProvider.System));
        services.AddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.AddLogging();
        services.AddCloudflareR2BlobStorage(configuration.GetSection("R2"));

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value.AccountId.Should().Be("acc123");
    }

    [Fact]
    public void invalid_options_fail_validation()
    {
        using var sp = _BuildProvider(options =>
        {
            options.AccountId = "";
            options.AccessKeyId = "";
            options.SecretAccessKey = "";
        });

        var act = () => sp.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }
}
