// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.CloudflareR2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Verifies the <c>AddHeadlessBlobs(blobs =&gt; blobs.UseCloudflareR2(…))</c> default-store registration: options
/// binding, validation, and that the store resolves as a presigned-capable <see cref="IBlobStorage"/>. The R2
/// client configuration is covered by the shared <c>R2ClientFactory</c> and by
/// <c>CloudflareR2BlobsRegistrationTests</c>; the R2 naming normalizer and options shape have dedicated unit
/// tests. No network I/O is performed.
/// </summary>
public sealed class SetupCloudflareR2BlobTests
{
    private static ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();

        return services;
    }

    private static void _ConfigureR2(R2BlobStorageOptions options)
    {
        options.AccountId = "acc123";
        options.AccessKeyId = "key";
        options.SecretAccessKey = "secret";
    }

    [Fact]
    public async Task default_r2_store_resolves_as_presigned_capable_blob_storage()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessBlobs(blobs => blobs.UseCloudflareR2(_ConfigureR2));

        // AwsBlobStorage is IAsyncDisposable, so the provider must be disposed asynchronously once resolved.
        await using var sp = services.BuildServiceProvider();

        // when
        var storage = sp.GetRequiredService<IBlobStorage>();

        // then — the reused AWS engine implements IPresignedUrlBlobStorage, but no global alias is registered
        storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        sp.GetService<IPresignedUrlBlobStorage>().Should().BeNull();
    }

    [Fact]
    public void binds_options_from_configuration_section()
    {
        // given
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

        var services = _CreateServices();
        services.AddHeadlessBlobs(blobs => blobs.UseCloudflareR2(configuration.GetSection("R2")));

        using var sp = services.BuildServiceProvider();

        // then
        sp.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value.AccountId.Should().Be("acc123");
    }

    [Fact]
    public void invalid_options_fail_validation()
    {
        // given
        var services = _CreateServices();
        services.AddHeadlessBlobs(blobs =>
            blobs.UseCloudflareR2(options =>
            {
                options.AccountId = "";
                options.AccessKeyId = "";
                options.SecretAccessKey = "";
            })
        );

        using var sp = services.BuildServiceProvider();

        // when
        var act = () => sp.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }
}
