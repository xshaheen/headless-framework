// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Tests.Azure;

public sealed class SetupTusAzureStoreTests : TestBase
{
    // Constructing BlobServiceClient from the well-known dev connection string makes no network
    // calls, and neither does the store constructor.
    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));

        return services;
    }

    public static TheoryData<string> Overloads => ["configuration", "action", "action-with-provider"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public void should_resolve_store_and_host_store_as_one_singleton(string overload)
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus =>
        {
            switch (overload)
            {
                case "configuration":
                    tus.UseAzure(
                        new ConfigurationBuilder()
                            .AddInMemoryCollection(
                                new Dictionary<string, string?>(StringComparer.Ordinal)
                                {
                                    ["ContainerName"] = "my-uploads",
                                }
                            )
                            .Build()
                    );
                    break;
                case "action":
                    tus.UseAzure(options => options.ContainerName = "my-uploads");
                    break;
                default:
                    tus.UseAzure((options, _) => options.ContainerName = "my-uploads");
                    break;
            }
        });
        using var provider = services.BuildServiceProvider();

        // when
        var store = provider.GetRequiredService<TusAzureStore>();

        // then
        provider.GetRequiredService<TusAzureStore>().Should().BeSameAs(store);
        provider.GetRequiredService<ITusStore>().Should().BeSameAs(store);
        provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value.ContainerName.Should().Be("my-uploads");
    }

    [Fact]
    public void should_bind_options_from_configuration()
    {
        // given
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ContainerName"] = "my-uploads",
                    ["BlobPrefix"] = "tenant-a/",
                }
            )
            .Build();

        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseAzure(config));
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value;

        // then
        options.ContainerName.Should().Be("my-uploads");
        options.BlobPrefix.Should().Be("tenant-a/");
    }

    [Fact]
    public void should_bind_options_via_service_provider_overload()
    {
        // given - the provider-factory overload resolves a registered marker to derive options,
        // exercising the UseAzure(Action<TOptions, IServiceProvider>) wiring path.
        var services = _Services();
        services.AddSingleton(new ContainerNameSource("provider-derived-uploads"));
        services.AddHeadlessTus(tus =>
            tus.UseAzure(
                (options, provider) =>
                {
                    options.ContainerName = provider.GetRequiredService<ContainerNameSource>().Name;
                }
            )
        );
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value;

        // then
        options.ContainerName.Should().Be("provider-derived-uploads");
    }

    [Fact]
    public void should_register_default_headers_provider()
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseAzure(_ => { }));
        using var provider = services.BuildServiceProvider();

        // when / then
        provider
            .GetRequiredService<ITusAzureBlobHttpHeadersProvider>()
            .Should()
            .BeOfType<DefaultTusAzureBlobHttpHeadersProvider>();
    }

    [Fact]
    public void should_honor_pre_registered_headers_provider()
    {
        // given - a custom provider registered before AddHeadlessTus wins (TryAdd semantics)
        var services = _Services();
        services.AddSingleton<ITusAzureBlobHttpHeadersProvider, FakeHeadersProvider>();
        services.AddHeadlessTus(tus => tus.UseAzure(_ => { }));
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetRequiredService<ITusAzureBlobHttpHeadersProvider>().Should().BeOfType<FakeHeadersProvider>();
        provider.GetRequiredService<TusAzureStore>();
    }

    [Fact]
    public void should_fail_resolution_for_invalid_options()
    {
        // given - empty container name violates the FluentValidation rules
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseAzure(options => options.ContainerName = ""));
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusAzureStore>();

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_fail_resolution_without_blob_service_client()
    {
        // given - UseAzure requires the app to register BlobServiceClient
        var services = new ServiceCollection();
        services.AddHeadlessTus(tus => tus.UseAzure(_ => { }));
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusAzureStore>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*BlobServiceClient*");
    }

    [Fact]
    public void should_register_container_as_provisioned_resource()
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseAzure(options => options.ContainerName = "my-uploads"));
        using var provider = services.BuildServiceProvider();

        // when
        var provisioners = provider.GetServices<IResourceProvisioner>().ToList();

        // then
        provisioners.Should().ContainSingle();
        provisioners[0].Kind.Should().Be("AzureBlob");
        provisioners[0].Name.Should().Be("my-uploads");
        provisioners[0].StartupCheck.Should().Be(ResourceStartupCheck.GateReadiness);
    }

    private sealed record ContainerNameSource(string Name);

    private sealed class FakeHeadersProvider : ITusAzureBlobHttpHeadersProvider
    {
        public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
        {
            return ValueTask.FromResult(new BlobHttpHeaders { ContentType = "test/fake" });
        }
    }
}
