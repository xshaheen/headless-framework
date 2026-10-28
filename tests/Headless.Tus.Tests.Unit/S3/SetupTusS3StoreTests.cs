// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Tests.S3;

public sealed class SetupTusS3StoreTests : TestBase
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_s3);

        return services;
    }

    public static TheoryData<string> Overloads => ["configuration", "action", "action-with-provider"];

    private static void _Register(IServiceCollection services, string overload)
    {
        switch (overload)
        {
            case "configuration":
                services.AddTusS3Store(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Bucket"] = "uploads-bucket" }
                        )
                        .Build()
                );
                break;
            case "action":
                services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
                break;
            default:
                services.AddTusS3Store((options, _) => options.Bucket = "uploads-bucket");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void should_resolve_store_and_expiration_store_as_one_singleton(string overload)
    {
        // given
        var services = _Services();
        _Register(services, overload);
        using var provider = services.BuildServiceProvider();

        // when
        var store = provider.GetRequiredService<TusS3Store>();

        // then
        provider.GetRequiredService<TusS3Store>().Should().BeSameAs(store);
        provider.GetRequiredService<ITusExpirationStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOptions<TusS3StoreOptions>>().Value.Bucket.Should().Be("uploads-bucket");
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_use_the_registered_client_for_the_bucket_provisioner(string overload)
    {
        // given
        var services = _Services();
        _Register(services, overload);
        await using var provider = services.BuildServiceProvider();
        var provisioner = provider.GetServices<IResourceProvisioner>().Single();

        // when
        await provisioner.VerifyAsync(AbortToken);

        // then
        await _s3.Received(1)
            .ListObjectsV2Async(
                Arg.Is<ListObjectsV2Request>(r => r.BucketName == "uploads-bucket" && r.MaxKeys == 1),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_resolve_the_store_from_the_registered_client()
    {
        // given - a factory registration shows whether the store's construction asked the container for the client
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddSingleton(_ =>
        {
            resolutions++;

            return _s3;
        });
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        using var provider = services.BuildServiceProvider();

        // when
        provider.GetRequiredService<TusS3Store>();

        // then
        resolutions.Should().Be(1);
    }

    [Fact]
    public void should_make_no_storage_request_when_constructing_the_store()
    {
        // when
        _ = new TusS3Store(_s3, new TusS3StoreOptions { Bucket = "uploads-bucket" });

        // then
        _s3.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void should_register_the_bucket_as_a_provisioned_resource()
    {
        // given
        var services = _Services();
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        using var provider = services.BuildServiceProvider();

        // when
        var provisioners = provider.GetServices<IResourceProvisioner>().ToList();

        // then
        provisioners.Should().ContainSingle();
        provisioners[0].Kind.Should().Be("S3Bucket");
        provisioners[0].Name.Should().Be("uploads-bucket");
        provisioners[0].StartupCheck.Should().Be(ResourceStartupCheck.GateReadiness);
    }

    [Fact]
    public void should_register_one_provisioner_when_called_twice()
    {
        // given
        var services = _Services();
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetServices<IResourceProvisioner>().Should().ContainSingle();
    }

    [Fact]
    public void should_register_http_context_accessor()
    {
        // given
        var services = _Services();
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetService<IHttpContextAccessor>().Should().NotBeNull();
    }

    [Fact]
    public void should_fail_resolution_without_s3_client()
    {
        // given - the app registers IAmazonS3; the store never builds one
        var services = new ServiceCollection();
        services.AddTusS3Store(options => options.Bucket = "uploads-bucket");
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusS3Store>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*IAmazonS3*");
    }

    [Fact]
    public void should_fail_resolution_for_invalid_options()
    {
        // given - no bucket
        var services = _Services();
        services.AddTusS3Store(_ => { });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusS3Store>();

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_register_the_default_object_headers_provider()
    {
        // given
        var services = _Services();
        _Register(services, "action");
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<ITusS3ObjectHeadersProvider>()
            .Should()
            .BeOfType<DefaultTusS3ObjectHeadersProvider>();
    }

    [Fact]
    public void should_keep_an_object_headers_provider_registered_before_the_store()
    {
        // given
        var custom = Substitute.For<ITusS3ObjectHeadersProvider>();
        var services = _Services();
        services.AddSingleton(custom);
        _Register(services, "action");
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ITusS3ObjectHeadersProvider>().Should().BeSameAs(custom);
        provider.GetRequiredService<TusS3Store>().Should().NotBeNull();
    }
}
