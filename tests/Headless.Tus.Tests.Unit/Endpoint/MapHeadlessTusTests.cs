// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class MapHeadlessTusTests : TestBase
{
    private static TusDiskStore _DiskStore()
    {
        // Constructing the disk store touches no file: it only keeps the path.
        return new TusDiskStore(Path.GetTempPath());
    }

    private static TusEndpointTestHostSetup _WithMaxSize(long maxSize)
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) => tus.UseStore(store).AddUploadEndpoint(options => options.MaxSize = maxSize),
        };
    }

    [Fact]
    public async Task should_complete_an_upload_within_the_limit_through_post_and_patch()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var content = "hello world"u8.ToArray();

        // when
        using var created = await host.CreateAsync(content.Length);
        var location = created.Headers.Location!.ToString();
        using var patched = await host.PatchAsync(location, content, offset: 0);

        // then
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        location.Should().StartWith($"{TusEndpointTestHost.Route}/");
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");

        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("11");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");

        var file = await host.Store.GetFileAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);
        await using var stored = await file.GetContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_answer_413_when_the_declared_length_exceeds_the_maximum_size()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_WithMaxSize(100), AbortToken);

        // when
        using var atLimit = await host.CreateAsync(uploadLength: 100);
        using var aboveLimit = await host.CreateAsync(uploadLength: 101);

        // then
        atLimit.StatusCode.Should().Be(HttpStatusCode.Created);
        aboveLimit.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task should_answer_413_during_the_write_when_a_deferred_length_upload_exceeds_the_maximum_size()
    {
        // given - the length is not declared, so only the bytes that arrive can break the limit
        await using var host = await TusEndpointTestHost.StartAsync(_WithMaxSize(100), AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: null);

        // when
        using var patched = await host.PatchAsync(location, new byte[101], offset: 0);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task should_accept_a_single_patch_larger_than_the_servers_default_body_limit()
    {
        // given - Kestrel refuses a body above 30,000,000 bytes unless the limit is raised for the request
        const int length = 31 * 1024 * 1024;
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(store).AddUploadEndpoint(static options => options.MaxSize = 64L * 1024 * 1024),
            UseKestrel = true,
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(length);

        // when
        using var patched = await host.PatchAsync(location, new byte[length], offset: 0);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched
            .Headers.GetValues("Upload-Offset")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(length.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_serve_the_upload_when_the_servers_body_limit_cannot_be_changed()
    {
        // given - a server whose body limit is already locked for the request
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureMiddleware = static app =>
                app.Use(
                    static (context, next) =>
                    {
                        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new LockedBodySizeFeature());

                        return next(context);
                    }
                ),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var content = "hello world"u8.ToArray();

        // when
        var location = await host.CreateUploadAsync(content.Length);
        using var patched = await host.PatchAsync(location, content, offset: 0);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_advertise_the_stores_extensions_without_concatenation()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when
        using var request = new HttpRequestMessage(HttpMethod.Options, TusEndpointTestHost.Route);
        using var response = await host.Client.SendAsync(request, AbortToken);

        // then - the disk store supports concatenation, so only the endpoint can have removed it
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var extensions = response.Headers.GetValues("Tus-Extension").Should().ContainSingle().Which.Split(',');
        extensions.Should().NotContain("concatenation");
        extensions
            .Should()
            .Contain(["creation", "creation-with-upload", "creation-defer-length", "termination", "expiration"]);
        response.Headers.GetValues("Tus-Max-Size").Should().ContainSingle().Which.Should().Be("1048576");
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("final;/files/a /files/b")]
    public async Task should_not_accept_a_concatenation_request(string uploadConcat)
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when
        using var response = await host.CreateAsync(
            uploadLength: 10,
            configure: request => request.Headers.Add("Upload-Concat", uploadConcat)
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_an_incomplete_upload_for_24_hours_after_its_last_write_by_default()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when
        using var created = await host.CreateAsync(uploadLength: 100);
        var location = created.Headers.Location!.ToString();

        // then
        _ExpiresAt(created).Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));

        // given - the stored deadline is moved near, so only a write that restarts the window can move it back
        await host.Store.SetExpirationAsync(
            TusEndpointTestHost.FileIdOf(location),
            DateTimeOffset.UtcNow.AddHours(1),
            AbortToken
        );

        // when
        using var patched = await host.PatchAsync(location, new byte[10], offset: 0);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _ExpiresAt(patched).Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task should_serve_each_named_endpoint_with_its_own_limit()
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
            {
                tus.UseStore(store);
                tus.AddUploadEndpoint("avatars", static options => options.MaxSize = 10);
                tus.AddUploadEndpoint("videos", static options => options.MaxSize = 1000);
            },
            Map = static endpoints =>
            {
                endpoints.MapHeadlessTus("/avatars", "avatars");
                endpoints.MapHeadlessTus("/videos", "videos");
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var avatar = await host.CreateAsync(uploadLength: 500, route: "/avatars");
        using var video = await host.CreateAsync(uploadLength: 500, route: "/videos");

        // then
        avatar.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        video.StatusCode.Should().Be(HttpStatusCode.Created);
        video.Headers.Location!.ToString().Should().StartWith("/videos/");
    }

    [Fact]
    public async Task should_use_the_file_lock_provider_registered_in_the_container()
    {
        // given - a provider whose lock is always held by someone else
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureServices = static services => services.AddSingleton<ITusFileLockProvider, HeldLockProvider>(),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // when
        using var patched = await host.PatchAsync(location, new byte[10], offset: 0);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task should_throw_at_mapping_time_when_add_headless_tus_was_not_called()
    {
        // given
        await using var app = WebApplication.CreateSlimBuilder().Build();

        // when
        var mapDefault = () => app.MapHeadlessTus("/files");
        var mapNamed = () => app.MapHeadlessTus("/files", "videos");

        // then
        const string message =
            "Headless TUS is not registered. Call services.AddHeadlessTus(tus => …) before mapping an upload endpoint.";
        mapDefault.Should().Throw<InvalidOperationException>().WithMessage(message);
        mapNamed.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public async Task should_throw_at_mapping_time_when_the_named_endpoint_was_not_declared()
    {
        // given - only the default endpoint is declared
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHeadlessTus(tus =>
            tus.UseStore(_DiskStore()).AddUploadEndpoint(options => options.MaxSize = 100)
        );
        await using var app = builder.Build();

        // when
        var act = () => app.MapHeadlessTus("/files", "videos");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "The upload endpoint \"videos\" is not declared. Call tus.AddUploadEndpoint(\"videos\", …) inside "
                    + "AddHeadlessTus."
            );
    }

    [Fact]
    public async Task should_throw_at_mapping_time_when_the_default_endpoint_was_not_declared()
    {
        // given - only a named endpoint is declared
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHeadlessTus(tus =>
            tus.UseStore(_DiskStore()).AddUploadEndpoint("videos", options => options.MaxSize = 100)
        );
        await using var app = builder.Build();

        // when
        var act = () => app.MapHeadlessTus("/files");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "The default upload endpoint is not declared. Call tus.AddUploadEndpoint(options => …) inside "
                    + "AddHeadlessTus, or pass the name of a declared endpoint to MapHeadlessTus."
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task should_reject_a_blank_endpoint_name_when_mapping(string name)
    {
        // given
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHeadlessTus(tus =>
            tus.UseStore(_DiskStore()).AddUploadEndpoint(options => options.MaxSize = 100)
        );
        await using var app = builder.Build();

        // when
        var act = () => app.MapHeadlessTus("/files", name);

        // then - the default endpoint has its own overload, so a blank name is a mistake, not a way to reach it
        act.Should().Throw<ArgumentException>();
    }

    private static DateTimeOffset _ExpiresAt(HttpResponseMessage response)
    {
        var value = response.Headers.GetValues("Upload-Expires").Should().ContainSingle().Which;

        return DateTimeOffset.ParseExact(value, "R", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private sealed class LockedBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => true;

        public long? MaxRequestBodySize
        {
            get => 1;
            set => throw new InvalidOperationException("The body limit is read-only once the body is being read.");
        }
    }

    private sealed class HeldLockProvider : ITusFileLockProvider
    {
        public Task<ITusFileLock> AquireLock(string fileId)
        {
            return Task.FromResult<ITusFileLock>(new HeldLock());
        }

        private sealed class HeldLock : ITusFileLock
        {
            public Task<bool> Lock()
            {
                return Task.FromResult(false);
            }

            public Task ReleaseIfHeld()
            {
                return Task.CompletedTask;
            }
        }
    }
}
