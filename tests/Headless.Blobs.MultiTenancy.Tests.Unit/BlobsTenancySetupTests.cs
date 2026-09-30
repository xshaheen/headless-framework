// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Registration of the blob tenancy seam: ordering, named stores, capability forwards, and startup checks.</summary>
public sealed class BlobsTenancySetupTests : TestBase
{
    private const string _Named = "docs";
    private const string _Shared = "shared";
    private static readonly BlobLocation _Avatar = new("avatars", "1.png");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_scope_default_and_named_stores_whichever_registration_runs_first(bool seamFirst)
    {
        // given
        var defaultStore = Substitute.For<IBlobStorage>();
        var namedStore = Substitute.For<IBlobStorage>();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        void addBlobs() =>
            builder.Services.AddHeadlessBlobs(blobs =>
            {
                blobs.RegisterDefaultProvider(s => s.AddSingleton(defaultStore));
                blobs.AddNamed(_Named, i => i.RegisterProvider(s => s.AddKeyedSingleton(_Named, namedStore)));
            });

        void addTenancy() => builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));

        if (seamFirst)
        {
            addTenancy();
            addBlobs();
        }
        else
        {
            addBlobs();
            addTenancy();
        }

        await using var provider = builder.Services.BuildServiceProvider();
        var tenant = provider.GetRequiredService<ICurrentTenant>();

        // when
        using (tenant.Change("acme"))
        {
            await provider.GetRequiredService<IBlobStorage>().ExistsAsync(_Avatar, AbortToken);
            await provider
                .GetRequiredService<IBlobStorageProvider>()
                .GetStorage(_Named)
                .ExistsAsync(_Avatar, AbortToken);
        }

        // then
        var scoped = new BlobLocation("avatars", "acme/1.png");
        await defaultStore.Received(1).ExistsAsync(scoped, AbortToken);
        await namedStore.Received(1).ExistsAsync(scoped, AbortToken);
    }

    [Fact]
    public async Task should_leave_an_unscoped_named_store_undecorated()
    {
        // given
        var sharedStore = Substitute.For<IBlobStorage>();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHeadlessBlobs(blobs =>
            blobs.AddNamed(_Shared, i => i.RegisterProvider(s => s.AddKeyedSingleton(_Shared, sharedStore)))
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant(o => o.UnscopedStores.Add(_Shared))));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var resolved = provider.GetRequiredKeyedService<IBlobStorage>(_Shared);

        // then
        resolved.Should().BeSameAs(sharedStore);
    }

    [Fact]
    public async Task should_route_the_keyed_presigned_forward_through_the_scoped_store()
    {
        // given
        var namedStore = Substitute.For<IBlobStorage, IPresignedUrlBlobStorage>();
        ((IPresignedUrlBlobStorage)namedStore)
            .GetPresignedDownloadUrlAsync(Arg.Any<BlobLocation>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://signed"));
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHeadlessBlobs(blobs =>
            blobs.AddNamed(
                _Named,
                i =>
                    i.RegisterProvider(s =>
                    {
                        s.AddKeyedSingleton(_Named, namedStore);

                        // The forward every presign-capable provider registers for a named store.
                        s.AddKeyedSingleton<IPresignedUrlBlobStorage>(
                            _Named,
                            (sp, _) => (IPresignedUrlBlobStorage)sp.GetRequiredKeyedService<IBlobStorage>(_Named)
                        );
                    })
            )
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));

        await using var provider = builder.Services.BuildServiceProvider();
        var presigned = provider.GetRequiredKeyedService<IPresignedUrlBlobStorage>(_Named);

        // when
        using (provider.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
            await presigned.GetPresignedDownloadUrlAsync(_Avatar, TimeSpan.FromMinutes(1), AbortToken);
        }

        // then
        await ((IPresignedUrlBlobStorage)namedStore)
            .Received(1)
            .GetPresignedDownloadUrlAsync(
                new BlobLocation("avatars", "acme/1.png"),
                TimeSpan.FromMinutes(1),
                AbortToken
            );
    }

    [Fact]
    public void should_record_the_blob_seam_as_guarded()
    {
        // given
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));

        // then
        var seam = _Manifest(builder).GetSeam(HeadlessBlobsTenancyBuilder.Seam);
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Guarded);
        seam.Capabilities.Should().Contain(HeadlessBlobsTenancyBuilder.ScopeByTenantCapability);
    }

    [Fact]
    public async Task should_report_a_startup_error_when_no_store_was_scoped()
    {
        // given
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var diagnostics = _Validate(provider);

        // then
        diagnostics
            .Should()
            .ContainSingle(d => d.Code == "HEADLESS_TENANCY_BLOBS_NO_SCOPED_STORE")
            .Which.Severity.Should()
            .Be(HeadlessTenancyDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task should_report_nothing_when_a_store_was_scoped()
    {
        // given
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));
        builder.Services.AddHeadlessBlobs(blobs =>
            blobs.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()))
        );

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var diagnostics = _Validate(provider);

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_an_invalid_container_prefix_at_options_validation()
    {
        // given
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHeadlessBlobs(blobs =>
            blobs.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()))
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant(o => o.ContainerPrefix = "App_")));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var action = () => provider.GetRequiredService<IBlobStorage>();

        // then
        action
            .Should()
            .Throw<Microsoft.Extensions.Options.OptionsValidationException>()
            .WithMessage("*ContainerPrefix*");
    }

    private static TenantPostureManifest _Manifest(IHostApplicationBuilder builder)
    {
        return (TenantPostureManifest)
            builder.Services.Single(d => d.ServiceType == typeof(TenantPostureManifest)).ImplementationInstance!;
    }

    private static List<HeadlessTenancyDiagnostic> _Validate(IServiceProvider provider)
    {
        var context = new HeadlessTenancyValidationContext(
            provider,
            provider.GetRequiredService<TenantPostureManifest>()
        );

        return
        [
            .. provider.GetServices<IHeadlessTenancyValidator>().SelectMany(validator => validator.Validate(context)),
        ];
    }
}
