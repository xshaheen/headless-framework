// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Blobs.CloudflareR2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration for Cloudflare R2 (reusing the S3 engine), and
/// that R2's forced behavior settings are bound per-instance and do not leak into a coexisting AWS store.
/// Registration-shape only — client construction is lazy and no network calls are made.
/// </summary>
public sealed class CloudflareR2BlobsRegistrationTests
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();

        return services;
    }

    private static void ConfigureR2(R2BlobStorageOptions options)
    {
        options.AccountId = "test-account";
        options.AccessKeyId = "test-access-key";
        options.SecretAccessKey = "test-secret-key";
    }

    [Fact]
    public async Task default_and_named_r2_stores_resolve_and_expose_presigned()
    {
        // given
        var services = CreateServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseCloudflareR2(ConfigureR2);
            blobs.AddNamed("images", instance => instance.UseCloudflareR2(ConfigureR2));
            blobs.AddNamed("docs", instance => instance.UseCloudflareR2(ConfigureR2));
        });
        await using var sp = services.BuildServiceProvider();

        // when
        var defaultStorage = sp.GetService<IBlobStorage>();
        var provider = sp.GetRequiredService<IBlobStorageProvider>();
        var images = provider.GetStorage("images");
        var docs = provider.GetStorage("docs");

        // then
        defaultStorage.Should().NotBeNull();
        defaultStorage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        sp.GetService<IPresignedUrlBlobStorage>().Should().BeNull();
        images.Should().NotBeSameAs(docs);
        sp.GetRequiredKeyedService<IBlobStorage>("images").Should().BeSameAs(images);
        images.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        sp.GetRequiredKeyedService<IPresignedUrlBlobStorage>("images").Should().BeSameAs(images);
    }

    [Fact]
    public async Task r2_forced_defaults_are_isolated_from_a_coexisting_aws_store()
    {
        // given — an R2 named store and an AWS named store side by side
        var services = CreateServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed("r2store", instance => instance.UseCloudflareR2(ConfigureR2));
            blobs.AddNamed("awsstore", instance => instance.UseAws(options => options.DisablePayloadSigning = false));
        });
        await using var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>();

        // then — R2 forces DisablePayloadSigning on its own named options; the AWS store keeps its own value
        monitor.Get("r2store").DisablePayloadSigning.Should().BeTrue();
        monitor.Get("awsstore").DisablePayloadSigning.Should().BeFalse();

        // and — a named-only (no default) setup leaks no unkeyed presigned alias
        sp.GetService<IPresignedUrlBlobStorage>().Should().BeNull();
    }

    [Fact]
    public async Task default_r2_does_not_mutate_unnamed_or_named_aws_options()
    {
        // given — R2 as the DEFAULT store, plus a named AWS store
        const string collidingName = "__headless_blobs_r2_default";

        var services = CreateServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseCloudflareR2(ConfigureR2);
            blobs.AddNamed(collidingName, instance => instance.UseAws(options => { }));
        });
        await using var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>();

        // then — the default R2 store uses a private options snapshot, so neither the shared unnamed
        // AwsBlobStorageOptions nor an internal-looking named AWS store inherits R2's DisablePayloadSigning = true.
        sp.GetRequiredService<IOptions<AwsBlobStorageOptions>>().Value.DisablePayloadSigning.Should().BeFalse();
        monitor.Get(collidingName).DisablePayloadSigning.Should().BeFalse();
    }
}
