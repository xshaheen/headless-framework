// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Azure;

public sealed class SetupTusAzureStoreTests : TestBase
{
    // Constructing BlobServiceClient from the well-known dev connection string makes no network
    // calls; combined with CreateContainerIfNotExists = false the store constructor is offline.
    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));

        return services;
    }

    [Fact]
    public void should_register_store_as_singleton()
    {
        // given
        var services = _Services();
        services.AddTusAzureStore(options => options.CreateContainerIfNotExists = false);
        using var provider = services.BuildServiceProvider();

        // when
        var store = provider.GetRequiredService<TusAzureStore>();

        // then
        store.Should().NotBeNull();
        provider.GetRequiredService<TusAzureStore>().Should().BeSameAs(store);
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
                    ["CreateContainerIfNotExists"] = "false",
                }
            )
            .Build();

        var services = _Services();
        services.AddTusAzureStore(config);
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TusAzureStoreOptions>>().Value;

        // then
        options.ContainerName.Should().Be("my-uploads");
        options.BlobPrefix.Should().Be("tenant-a/");
        options.CreateContainerIfNotExists.Should().BeFalse();
    }

    [Fact]
    public void should_bind_options_via_service_provider_overload()
    {
        // given - the provider-factory overload resolves a registered marker to derive options,
        // exercising the AddTusAzureStore(Action<TOptions, IServiceProvider>) wiring path.
        var services = _Services();
        services.AddSingleton(new ContainerNameSource("provider-derived-uploads"));
        services.AddTusAzureStore(
            (options, provider) =>
            {
                options.ContainerName = provider.GetRequiredService<ContainerNameSource>().Name;
                options.CreateContainerIfNotExists = false;
            }
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
        services.AddTusAzureStore(options => options.CreateContainerIfNotExists = false);
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
        // given - a custom provider registered before AddTusAzureStore wins (TryAdd semantics)
        var services = _Services();
        services.AddSingleton<ITusAzureBlobHttpHeadersProvider, FakeHeadersProvider>();
        services.AddTusAzureStore(options => options.CreateContainerIfNotExists = false);
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetRequiredService<ITusAzureBlobHttpHeadersProvider>().Should().BeOfType<FakeHeadersProvider>();
        provider.GetRequiredService<TusAzureStore>().Should().NotBeNull();
    }

    [Fact]
    public void should_fail_resolution_for_invalid_options()
    {
        // given - empty container name violates the FluentValidation rules
        var services = _Services();
        services.AddTusAzureStore(options =>
        {
            options.ContainerName = "";
            options.CreateContainerIfNotExists = false;
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusAzureStore>();

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_fail_resolution_without_blob_service_client()
    {
        // given - AddTusAzureStore requires the app to register BlobServiceClient
        var services = new ServiceCollection();
        services.AddTusAzureStore(options => options.CreateContainerIfNotExists = false);
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<TusAzureStore>();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*BlobServiceClient*");
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
