// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupBlobSignedUrlTests : TestBase
{
    [Fact]
    public async Task should_expose_presign_capability_on_default_and_named_stores_without_native_presign()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);

        // then
        app.DefaultStorage.Should()
            .BeAssignableTo<IPresignedUrlBlobStorage>()
            .Which.SupportedUploadConstraints.Should()
            .Be(PresignedUploadConstraintKinds.ContentType | PresignedUploadConstraintKinds.MaxLength);
        app.NamedStorage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        app.App.Services.GetRequiredKeyedService<IPresignedUrlBlobStorage>(SignedUrlTestApp.NamedStore)
            .Should()
            .BeSameAs(app.NamedStorage);
    }

    [Fact]
    public async Task should_not_wrap_store_when_it_has_native_presign()
    {
        // given
        var native = Substitute.For<IBlobStorage, IPresignedUrlBlobStorage>();
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(native));
            setup.AddNamed("cloud", instance => instance.RegisterProvider(s => s.AddKeyedSingleton("cloud", native)));
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("https://api.example.com"));
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IBlobStorage>().Should().BeSameAs(native);
        provider.GetRequiredKeyedService<IBlobStorage>("cloud").Should().BeSameAs(native);
        provider.GetRequiredKeyedService<IPresignedUrlBlobStorage>("cloud").Should().BeSameAs(native);
    }

    [Fact]
    public async Task should_point_minted_url_at_base_url_and_route_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options =>
            {
                options.BaseUrl = new Uri("https://example.com/app/");
                options.RoutePrefix = "/files/";
            });
        });
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var provider = services.BuildServiceProvider();
        var presigned = (IPresignedUrlBlobStorage)provider.GetRequiredService<IBlobStorage>();

        // when
        var url = await presigned.GetPresignedDownloadUrlAsync(
            new BlobLocation("c", "a.txt"),
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then
        url.GetLeftPart(UriPartial.Path).Should().StartWith("https://example.com/app/files/");
        url.Segments.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(null, "/blobs")]
    [InlineData("relative/path", "/blobs")]
    [InlineData("ftp://example.com", "/blobs")]
    [InlineData("https://example.com?x=1", "/blobs")]
    [InlineData("https://example.com", "blobs")]
    [InlineData("https://example.com", "/")]
    [InlineData("https://example.com", "/blobs/{id}")]
    public void should_reject_invalid_options(string? baseUrl, string routePrefix)
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(setup =>
        {
            setup.RegisterDefaultProvider(s => s.AddSingleton(Substitute.For<IBlobStorage>()));
            setup.UseSignedUrlEndpoint(options =>
            {
                options.BaseUrl = baseUrl is null ? null : new Uri(baseUrl, UriKind.RelativeOrAbsolute);
                options.RoutePrefix = routePrefix;
            });
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<BlobSignedUrlOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task should_reject_invalid_upload_constraints()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var presigned = (IPresignedUrlBlobStorage)app.DefaultStorage;
        var location = new BlobLocation(SignedUrlTestApp.Container, "a.txt");

        // when
        var badType = async () =>
            await presigned.GetPresignedUploadUrlAsync(
                location,
                TimeSpan.FromMinutes(1),
                new PresignedUploadConstraints { ContentType = "not a media type" },
                AbortToken
            );
        var wildcardType = async () =>
            await presigned.GetPresignedUploadUrlAsync(
                location,
                TimeSpan.FromMinutes(1),
                new PresignedUploadConstraints { ContentType = "image/*" },
                AbortToken
            );
        var badLength = async () =>
            await presigned.GetPresignedUploadUrlAsync(
                location,
                TimeSpan.FromMinutes(1),
                new PresignedUploadConstraints { MaxLength = 0 },
                AbortToken
            );
        var badExpiry = async () => await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.Zero, AbortToken);

        // then
        await badType.Should().ThrowAsync<ArgumentException>();
        await wildcardType.Should().ThrowAsync<ArgumentException>();
        await badLength.Should().ThrowAsync<ArgumentException>();
        await badExpiry.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_mapping_endpoint_without_registration()
    {
        // given
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        // when
        var act = () => app.MapBlobSignedUrlEndpoint();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UseSignedUrlEndpoint*");
    }
}
