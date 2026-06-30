// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for the <c>AddHeadlessBlobs</c> setup builder gates and extension application.</summary>
public sealed class BlobsSetupBuilderTests
{
    [Fact]
    public void should_allow_setup_with_no_default_and_register_provider()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessBlobs(static _ => { });

        // then
        services.Should().Contain(static descriptor => descriptor.ServiceType == typeof(IBlobStorageProvider));
    }

    [Fact]
    public void should_reject_setup_when_multiple_default_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessBlobs(setup =>
            {
                setup.RegisterDefaultProvider(static _ => { });
                setup.RegisterDefaultProvider(static _ => { });
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*at most one default*");
    }

    [Fact]
    public void should_reject_repeated_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup => setup.RegisterDefaultProvider(static _ => { }));

        // when
        var action = () => services.AddHeadlessBlobs(setup => setup.RegisterDefaultProvider(static _ => { }));

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_apply_extensions_in_order_default_then_named_then_cross_cutting()
    {
        // given
        var services = new ServiceCollection();
        var log = new List<string>();

        // when
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterCrossCuttingExtension(_ => log.Add("cross-cutting"));
            setup.AddNamed("docs", instance => instance.RegisterProvider(_ => log.Add("named")));
            setup.RegisterDefaultProvider(_ => log.Add("default"));
        });

        // then
        log.Should().Equal("default", "named", "cross-cutting");
        services.Should().Contain(static descriptor => descriptor.ServiceType == typeof(IBlobStorageProvider));
    }

    [Fact]
    public void should_not_register_services_when_gate_throws()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessBlobs(setup =>
            {
                setup.RegisterDefaultProvider(static _ => { });
                setup.RegisterDefaultProvider(static _ => { });
            });

        // then
        action.Should().Throw<InvalidOperationException>();
        services.Should().BeEmpty();
    }

    [Fact]
    public void add_named_should_reject_duplicate_names()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessBlobs(setup =>
            {
                setup.AddNamed("docs", instance => instance.RegisterProvider(static _ => { }));
                setup.AddNamed("docs", instance => instance.RegisterProvider(static _ => { }));
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'docs'*already configured*");
    }

    [Fact]
    public void add_named_should_reject_whitespace_name()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessBlobs(setup =>
                setup.AddNamed(" ", instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void add_named_should_reject_zero_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessBlobs(setup => setup.AddNamed("docs", static _ => { }));

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'docs' requires exactly one provider*");
    }

    [Fact]
    public void add_named_should_reject_multiple_providers()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessBlobs(setup =>
                setup.AddNamed(
                    "docs",
                    instance =>
                    {
                        instance.RegisterProvider(static _ => { });
                        instance.RegisterProvider(static _ => { });
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*'docs'*");
    }

    [Fact]
    public async Task named_extension_keyed_registration_should_be_reachable_via_blob_storage_provider()
    {
        // given
        var namedStorage = Substitute.For<IBlobStorage>();
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
            setup.AddNamed(
                "images",
                instance =>
                {
                    var name = instance.Name;
                    instance.RegisterProvider(svc => svc.AddKeyedSingleton(name, namedStorage));
                }
            )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var blobProvider = provider.GetRequiredService<IBlobStorageProvider>();

        // then
        blobProvider.GetStorage("images").Should().BeSameAs(namedStorage);
        blobProvider.GetStorageOrNull("images").Should().BeSameAs(namedStorage);
    }

    [Fact]
    public async Task blob_storage_provider_should_throw_for_unknown_name_and_return_null_from_or_null()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(static _ => { });
        await using var provider = services.BuildServiceProvider();
        var blobProvider = provider.GetRequiredService<IBlobStorageProvider>();

        // when
        var action = () => blobProvider.GetStorage("missing");

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'missing'*");
        blobProvider.GetStorageOrNull("missing").Should().BeNull();
    }

    [Fact]
    public async Task named_only_setup_should_not_register_default_blob_storage()
    {
        // given
        var namedStorage = Substitute.For<IBlobStorage>();
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
            setup.AddNamed(
                "images",
                instance =>
                {
                    var name = instance.Name;
                    instance.RegisterProvider(svc => svc.AddKeyedSingleton(name, namedStorage));
                }
            )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var defaultStorage = provider.GetService<IBlobStorage>();

        // then
        defaultStorage.Should().BeNull();
    }

    [Fact]
    public void repeated_registration_should_not_mutate_the_service_collection_on_the_second_call()
    {
        // given — a successful first registration
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup => setup.RegisterDefaultProvider(static _ => { }));
        var descriptorCountAfterFirstCall = services.Count;

        // when — a second call hits the already-called gate
        var action = () => services.AddHeadlessBlobs(setup => setup.RegisterDefaultProvider(static _ => { }));

        // then — it throws before appending anything, so the collection is unchanged from the first call
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
        services.Should().HaveCount(descriptorCountAfterFirstCall);
    }

    [Fact]
    public async Task blob_storage_provider_should_reject_empty_name()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(static _ => { });
        await using var provider = services.BuildServiceProvider();
        var blobProvider = provider.GetRequiredService<IBlobStorageProvider>();

        // when
        var getStorage = () => blobProvider.GetStorage("");
        var getStorageOrNull = () => blobProvider.GetStorageOrNull("");

        // then — both guard the empty name via Argument.IsNotNullOrEmpty
        getStorage.Should().Throw<ArgumentException>();
        getStorageOrNull.Should().Throw<ArgumentException>();
    }
}
