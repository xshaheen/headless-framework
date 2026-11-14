// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.FileLocks;
using tusdotnet.Interfaces;

namespace Tests.Endpoint;

/// <summary>
/// What an application gets when it asks <see cref="ITusUploads"/> for an upload, and what a client may still do
/// to an upload once it is finished. The application's side runs through an endpoint of its own, mapped beside
/// the upload endpoint, so a lookup resolves its caller from a real request.
/// </summary>
public sealed class TusUploadsTests : TestBase
{
    private const string _Route = TusEndpointTestHost.Route;
    private const string _Claims = "/claims";

    private static TusEndpointTestHostSetup _Setup(
        Action<HeadlessTusSetupBuilder, tusdotnet.Stores.TusDiskStore>? configureTus = null,
        bool registerCurrentTenant = false,
        bool scopeByTenant = false,
        Action<IEndpointRouteBuilder>? mapUploads = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureServices = configureServices,
            ConfigureTus =
                configureTus
                ?? (
                    static (tus, store) =>
                        tus.UseStore(store)
                            .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize)
                ),
            RegisterCurrentTenant = registerCurrentTenant,
            ConfigureTenancy = scopeByTenant ? static tenancy => tenancy.Tus(static tus => tus.ScopeByTenant()) : null,
            Map = endpoints =>
            {
                if (mapUploads is null)
                {
                    endpoints.MapHeadlessTus(_Route);
                }
                else
                {
                    mapUploads(endpoints);
                }

                endpoints.MapGet(_Claims + "/{fileId}", _FindAsync);
                endpoints.MapDelete(_Claims + "/{fileId}", _FindAndDeleteAsync);
            },
        };
    }

    [Fact]
    public async Task should_give_the_creator_its_finished_upload_with_its_content()
    {
        // given - alice's finished upload, created in a tenant with metadata of her own
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(registerCurrentTenant: true), AbortToken);
        host.Tenant = "acme";
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content, metadata: "filename cmVwb3J0LnBkZg==,draft");

        // when - a request of hers names it
        var found = await _ClaimAsync(host, location);

        // then - she gets it, with what the endpoint recorded and nothing the endpoint added to her metadata
        found.Status.Should().Be(HttpStatusCode.OK);
        found.FileId.Should().Be(TusEndpointTestHost.FileIdOf(location));
        found.Length.Should().Be(300);
        found.OwnerKey.Should().Be("alice");
        found.TenantId.Should().Be("acme");
        found.EndpointName.Should().BeEmpty();
        found.Metadata.Should().Be("draft=|filename=report.pdf");
        found.Content.Should().Equal(content);
    }

    [Theory]
    [InlineData("bob", "acme")]
    [InlineData("alice", "globex")]
    [InlineData("alice", null)]
    [InlineData(null, "acme")]
    public async Task should_give_nothing_to_a_caller_the_upload_does_not_belong_to(string? user, string? tenant)
    {
        // given - alice's finished upload in the acme tenant
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(registerCurrentTenant: true), AbortToken);
        host.Tenant = "acme";
        var location = await _UploadAsync(host, Faker.Random.Bytes(300));

        // when - another owner, another tenant, a caller with no tenant, or a caller nobody is signed in as
        host.User = user;
        host.Tenant = tenant;
        var found = await _ClaimAsync(host, location);

        // then
        found.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_give_nothing_for_an_upload_of_another_endpoint()
    {
        // given - the default endpoint and a videos endpoint over one store, and a finished upload on each
        var setup = _Setup(
            configureTus: static (tus, store) =>
            {
                tus.UseStore(store);
                tus.AddUploadEndpoint(static options => options.MaxSize = 1000);
                tus.AddUploadEndpoint("videos", static options => options.MaxSize = 1000);
            },
            mapUploads: static endpoints =>
            {
                endpoints.MapHeadlessTus("/files");
                endpoints.MapHeadlessTus("/videos", "videos");
            }
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var file = await _UploadAsync(host, Faker.Random.Bytes(100), route: "/files");
        var video = await _UploadAsync(host, Faker.Random.Bytes(100), route: "/videos");

        // when - each upload is asked for under the other endpoint
        var fileAsVideo = await _ClaimAsync(host, file, endpoint: "videos");
        var videoAsFile = await _ClaimAsync(host, video);

        // then - neither is found there
        fileAsVideo.Status.Should().Be(HttpStatusCode.NotFound);
        videoAsFile.Status.Should().Be(HttpStatusCode.NotFound);

        // and each is found under its own endpoint
        var videoAsVideo = await _ClaimAsync(host, video, endpoint: "videos");
        videoAsVideo.Status.Should().Be(HttpStatusCode.OK);
        videoAsVideo.EndpointName.Should().Be("videos");
        (await _ClaimAsync(host, file)).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_give_nothing_for_an_upload_that_still_lacks_bytes()
    {
        // given - an upload with 100 of its 300 bytes
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await host.CreateUploadAsync(content.Length);
        (await host.PatchAsync(location, content[..100], offset: 0)).Dispose();
        var uploads = host.Services.GetRequiredService<ITusUploads>();
        var fileId = TusEndpointTestHost.FileIdOf(location);

        // when, then - neither lookup returns it
        (await _ClaimAsync(host, location))
            .Status.Should()
            .Be(HttpStatusCode.NotFound);
        (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().BeNull();

        // when - the rest arrives
        (await host.PatchAsync(location, content[100..], offset: 100)).Dispose();

        // then - both do
        (await _ClaimAsync(host, location))
            .Content.Should()
            .Equal(content);
        (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_give_nothing_for_an_upload_whose_length_was_never_declared()
    {
        // given - a deferred-length upload that holds bytes and no length
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: null);
        (await host.PatchAsync(location, Faker.Random.Bytes(100), offset: 0)).Dispose();

        // when
        var found = await _ClaimAsync(host, location);
        var trusted = await host
            .Services.GetRequiredService<ITusUploads>()
            .FindUncheckedAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);

        // then
        found.Status.Should().Be(HttpStatusCode.NotFound);
        trusted.Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-file-id")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public async Task should_give_nothing_for_an_id_no_upload_has(string fileId)
    {
        // given - an id the store could not have issued, and a well-formed one it never did
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);

        // when
        var found = await _ClaimAsync(host, $"{_Route}/{fileId}");
        var trusted = await host.Services.GetRequiredService<ITusUploads>().FindUncheckedAsync(fileId, AbortToken);

        // then
        found.Status.Should().Be(HttpStatusCode.NotFound);
        trusted.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_the_named_endpoint_was_never_declared()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var location = await _UploadAsync(host, Faker.Random.Bytes(100));

        // when
        var act = () => _ClaimAsync(host, location, endpoint: "videos");

        // then - a mistake in the application, not an answer about the upload
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"videos\" is not declared*");
    }

    [Fact]
    public async Task should_throw_when_the_caller_is_asked_for_outside_a_request()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var location = await _UploadAsync(host, Faker.Random.Bytes(100));
        var uploads = host.Services.GetRequiredService<ITusUploads>();

        // when - the test itself asks, as a background job would
        var act = () => uploads.FindAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);

        // then - the message names the lookup that needs no request
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FindUncheckedAsync*");
    }

    [Fact]
    public async Task should_give_trusted_code_any_owners_finished_upload_with_that_owners_key()
    {
        // given - bob's finished upload
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        host.User = "bob";
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content);

        // when - code that runs for nobody asks for it by id
        var upload = await host
            .Services.GetRequiredService<ITusUploads>()
            .FindUncheckedAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);

        // then - it gets the upload, and who it belongs to
        upload.Should().NotBeNull();
        upload.OwnerKey.Should().Be("bob");
        upload.TenantId.Should().BeNull();
        upload.EndpointName.Should().BeEmpty();
        upload.Length.Should().Be(300);
        (await _ReadAsync(upload)).Should().Equal(content);
    }

    [Fact]
    public async Task should_give_trusted_code_nothing_for_an_upload_no_endpoint_created()
    {
        // given - a finished upload written to the store directly, so it carries no owner
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var fileId = await host.Store.CreateFileAsync(100, metadata: null, AbortToken);
        await using (var content = new MemoryStream(Faker.Random.Bytes(100)))
        {
            await host.Store.AppendDataAsync(fileId, content, AbortToken);
        }

        // when
        var upload = await host.Services.GetRequiredService<ITusUploads>().FindUncheckedAsync(fileId, AbortToken);

        // then
        upload.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_trusted_code_inside_the_ambient_tenant_when_uploads_are_scoped_by_tenant()
    {
        // given - a finished upload of the acme tenant, on a store that keeps every tenant's uploads side by side
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(scopeByTenant: true), AbortToken);
        host.Tenant = "acme";
        var location = await _UploadAsync(host, Faker.Random.Bytes(100));
        var fileId = TusEndpointTestHost.FileIdOf(location);
        var uploads = host.Services.GetRequiredService<ITusUploads>();
        var tenant = host.Services.GetRequiredService<ICurrentTenant>();

        // when, then - another tenant's code does not get it
        using (tenant.Change("globex"))
        {
            (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().BeNull();
        }

        // and its own tenant's code does
        using (tenant.Change("acme"))
        {
            (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().NotBeNull();
        }

        // and code with no tenant is told so
        var act = () => uploads.FindUncheckedAsync(fileId, AbortToken);
        await act.Should().ThrowAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant()
    {
        // given - a finished upload of the acme tenant
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(scopeByTenant: true), AbortToken);
        host.Tenant = "acme";
        var location = await _UploadAsync(host, Faker.Random.Bytes(100));

        // when - a request that runs in no tenant names it
        host.Tenant = null;
        var found = await _ClaimAsync(host, location);

        // then - it is answered like any upload that is not the caller's, and the request does not fail
        found.Status.Should().Be(HttpStatusCode.NotFound);

        // and so is a request whose tenant id no upload could be stored under
        host.Tenant = "ACME";
        (await _ClaimAsync(host, location)).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_give_nothing_while_another_request_holds_the_upload()
    {
        // given - a finished upload whose file lock is held, as a request that writes to the upload holds it
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content);
        var fileLock = await InMemoryFileLockProvider.Instance.AquireLock(TusEndpointTestHost.FileIdOf(location));
        (await fileLock.Lock()).Should().BeTrue();

        try
        {
            // when, then - a lookup does not judge an upload a write may still change
            (await _ClaimAsync(host, location))
                .Status.Should()
                .Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await fileLock.ReleaseIfHeld();
        }

        // and once the lock is free the upload is found
        (await _ClaimAsync(host, location))
            .Content.Should()
            .Equal(content);
    }

    [Fact]
    public async Task should_give_nothing_for_an_upload_whose_last_write_failed_its_checksum()
    {
        // given - an upload whose last 200 bytes arrive with a checksum that does not match them
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await host.CreateUploadAsync(content.Length);
        (await host.PatchAsync(location, content[..100], offset: 0)).Dispose();
        using var refused = await _PatchWithChecksumAsync(host, location, content[100..], 100, Faker.Random.Bytes(20));

        // then - the store took the bytes back, so the upload is not finished
        ((int)refused.StatusCode)
            .Should()
            .Be(460);
        (await _ClaimAsync(host, location)).Status.Should().Be(HttpStatusCode.NotFound);

        // when - the same bytes arrive with their own checksum
#pragma warning disable CA5350 // Not a security use: sha1 is the checksum algorithm tus clients send and the disk store verifies.
        var checksum = SHA1.HashData(content[100..]);
#pragma warning restore CA5350
        using var accepted = await _PatchWithChecksumAsync(host, location, content[100..], 100, checksum);

        // then
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _ClaimAsync(host, location)).Content.Should().Equal(content);
    }

    [Fact]
    public async Task should_refuse_a_clients_delete_of_a_finished_upload()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content);

        // when - its own client asks to delete it
        using var delete = await host.DeleteAsync(location);

        // then - the request is refused, and the application still finds the upload
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _ClaimAsync(host, location)).Content.Should().Equal(content);
    }

    [Theory]
    [InlineData(300L)]
    [InlineData(null)]
    public async Task should_let_a_client_delete_an_upload_that_still_lacks_bytes(long? uploadLength)
    {
        // given - an upload that holds 100 bytes of a longer declared length, or of a length it never declared
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var location = await host.CreateUploadAsync(uploadLength);
        (await host.PatchAsync(location, Faker.Random.Bytes(100), offset: 0)).Dispose();

        // when
        using var delete = await host.DeleteAsync(location);

        // then
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_remove_the_upload_when_the_application_deletes_it()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var location = await _UploadAsync(host, Faker.Random.Bytes(300));

        // when - the application deletes the upload it found
        using var delete = await host.SendAsync(HttpMethod.Delete, _ClaimUri(location, endpoint: null));

        // then - it is gone for the application and for its client
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _ClaimAsync(host, location)).Status.Should().Be(HttpStatusCode.NotFound);

        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_treat_an_empty_upload_as_finished_from_its_creation()
    {
        // given - an endpoint that accepts empty uploads
        var setup = _Setup(
            configureTus: static (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowEmptyUploads = true;
                    })
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when - an upload of no bytes is created
        var location = await host.CreateUploadAsync(uploadLength: 0);

        // then - the application finds it at once
        var found = await _ClaimAsync(host, location);
        found.Status.Should().Be(HttpStatusCode.OK);
        found.Length.Should().Be(0);
        found.Content.Should().BeEmpty();

        // and its client cannot delete it
        using var delete = await host.DeleteAsync(location);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_say_so_when_the_store_cannot_return_or_delete_an_upload()
    {
        // given - a store that only creates uploads and takes their bytes
        var setup = _Setup(
            configureTus: static (tus, store) =>
                tus.UseStore(new CreationOnlyTusStore(store))
                    .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await _UploadAsync(host, Faker.Random.Bytes(100));
        var upload = await host
            .Services.GetRequiredService<ITusUploads>()
            .FindUncheckedAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);
        upload.Should().NotBeNull();

        // when
        var open = () => upload.OpenContentAsync(AbortToken);
        var delete = () => upload.DeleteAsync(AbortToken);

        // then - each names the capability the store lacks
        await open.Should().ThrowAsync<NotSupportedException>().WithMessage("*ITusReadableStore*");
        await delete.Should().ThrowAsync<NotSupportedException>().WithMessage("*ITusTerminationStore*");
    }

    [Fact]
    public async Task should_answer_not_found_to_another_owners_delete_of_a_finished_upload()
    {
        // given - alice's finished upload
        await using var host = await TusEndpointTestHost.StartAsync(_Setup(), AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content);

        // when - bob asks to delete it, and to delete an upload that never existed
        host.User = "bob";
        using var foreign = await host.DeleteAsync(location);
        using var unknown = await host.DeleteAsync($"{_Route}/{Guid.NewGuid():n}");

        // then - both are answered alike, so the refusal a finished upload's own client gets tells bob nothing
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TusEndpointTestHost.DescribeAsync(foreign, AbortToken))
            .Should()
            .Be(await TusEndpointTestHost.DescribeAsync(unknown, AbortToken));

        // and the upload is still alice's
        host.User = TusEndpointTestHost.DefaultUser;
        (await _ClaimAsync(host, location)).Content.Should().Equal(content);
    }

    [Fact]
    public async Task should_take_the_registered_file_lock_for_both_lookups()
    {
        // given - a host with a file lock provider of its own, and a finished upload
        var locks = new ScriptedLockProvider();
        var setup = _Setup(configureServices: services => services.AddSingleton<ITusFileLockProvider>(locks));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var content = Faker.Random.Bytes(300);
        var location = await _UploadAsync(host, content);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        var uploads = host.Services.GetRequiredService<ITusUploads>();

        // when - that provider reports the upload as held by another request
        locks.Held = true;

        // then - neither lookup returns it
        (await _ClaimAsync(host, location))
            .Status.Should()
            .Be(HttpStatusCode.NotFound);
        (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().BeNull();

        // when - it is free again
        locks.Held = false;

        // then - both do
        (await _ClaimAsync(host, location))
            .Content.Should()
            .Equal(content);
        (await uploads.FindUncheckedAsync(fileId, AbortToken)).Should().NotBeNull();
    }

    /// <summary>Creates an upload and sends every byte of <paramref name="content"/>, and returns its location.</summary>
    private static async Task<string> _UploadAsync(
        TusEndpointTestHost host,
        byte[] content,
        string? metadata = null,
        string route = _Route
    )
    {
        var location = await host.CreateUploadAsync(content.Length, metadata, route);
        using var patch = await host.PatchAsync(location, content, offset: 0);
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return location;
    }

    private static Task<HttpResponseMessage> _PatchWithChecksumAsync(
        TusEndpointTestHost host,
        string location,
        byte[] body,
        long offset,
        byte[] checksum
    )
    {
        return host.SendAsync(
            HttpMethod.Patch,
            location,
            request =>
            {
                request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
                request.Headers.Add("Upload-Checksum", $"sha1 {Convert.ToBase64String(checksum)}");
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            }
        );
    }

    private static string _ClaimUri(string location, string? endpoint)
    {
        var uri = $"{_Claims}/{TusEndpointTestHost.FileIdOf(location)}";

        return endpoint is null ? uri : $"{uri}?endpoint={endpoint}";
    }

    /// <summary>
    /// Asks the application's own endpoint for the upload at <paramref name="location"/>, as the host's current
    /// user and tenant.
    /// </summary>
    private static async Task<Claim> _ClaimAsync(TusEndpointTestHost host, string location, string? endpoint = null)
    {
        using var response = await host.SendAsync(HttpMethod.Get, _ClaimUri(location, endpoint));

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new Claim(response.StatusCode);
        }

        string header(string name) => response.Headers.GetValues(name).Single();

        return new Claim(response.StatusCode)
        {
            FileId = header("X-Upload-File-Id"),
            Length = long.Parse(header("X-Upload-Length"), CultureInfo.InvariantCulture),
            OwnerKey = header("X-Upload-Owner"),
            TenantId = response.Headers.TryGetValues("X-Upload-Tenant", out var tenant) ? tenant.Single() : null,
            EndpointName = header("X-Upload-Endpoint").Trim('[', ']'),
            Metadata = header("X-Upload-Metadata"),
            Content = await response.Content.ReadAsByteArrayAsync(AbortToken),
        };
    }

    private static async Task<byte[]> _ReadAsync(TusUpload upload)
    {
        await using var content = await upload.OpenContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, AbortToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// The application's endpoint: it asks for the upload the request names, on the endpoint the request names or
    /// on the default one, and answers with what it was given.
    /// </summary>
    private static async Task _FindAsync(HttpContext context)
    {
        var upload = await _FindForRequestAsync(context);

        if (upload is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var headers = context.Response.Headers;
        headers["X-Upload-File-Id"] = upload.FileId;
        headers["X-Upload-Length"] = upload.Length.ToString(CultureInfo.InvariantCulture);
        headers["X-Upload-Owner"] = upload.OwnerKey;
        headers["X-Upload-Endpoint"] = $"[{upload.EndpointName}]";
        headers["X-Upload-Metadata"] = string.Join(
            '|',
            upload
                .Metadata.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static e => $"{e.Key}={e.Value}")
        );

        if (upload.TenantId is not null)
        {
            headers["X-Upload-Tenant"] = upload.TenantId;
        }

        await using var content = await upload.OpenContentAsync(context.RequestAborted);
        await content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static async Task _FindAndDeleteAsync(HttpContext context)
    {
        var upload = await _FindForRequestAsync(context);

        if (upload is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        await upload.DeleteAsync(context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static Task<TusUpload?> _FindForRequestAsync(HttpContext context)
    {
        var uploads = context.RequestServices.GetRequiredService<ITusUploads>();
        var fileId = (string)context.Request.RouteValues["fileId"]!;
        var endpoint = context.Request.Query["endpoint"].ToString();

        return endpoint.Length == 0
            ? uploads.FindAsync(fileId, context.RequestAborted)
            : uploads.FindAsync(fileId, endpoint, context.RequestAborted);
    }

    /// <summary>
    /// A file lock provider of the application's own, which hands out tusdotnet's in-process locks until a scenario
    /// makes it report every upload as held by another request.
    /// </summary>
    private sealed class ScriptedLockProvider : ITusFileLockProvider
    {
        public bool Held { get; set; }

        public Task<ITusFileLock> AquireLock(string fileId)
        {
            return Held
                ? Task.FromResult<ITusFileLock>(new HeldLock())
                : InMemoryFileLockProvider.Instance.AquireLock(fileId);
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

    /// <summary>What the application's endpoint answered: the upload it was given, or only a status.</summary>
    private sealed record Claim(HttpStatusCode Status)
    {
        public string? FileId { get; init; }

        public long? Length { get; init; }

        public string? OwnerKey { get; init; }

        public string? TenantId { get; init; }

        public string? EndpointName { get; init; }

        public string? Metadata { get; init; }

        public byte[] Content { get; init; } = [];
    }
}
