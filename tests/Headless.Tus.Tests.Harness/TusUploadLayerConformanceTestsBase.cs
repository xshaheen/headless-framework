// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Context;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Tests;

/// <summary>
/// Runs an application's upload endpoint against the store under test: the endpoint is declared through
/// <c>AddHeadlessTus</c>, the store is selected through its provider's own registration, and a real server takes
/// the requests. The scenarios pin what only a real store can break: the round trip of the entries the endpoint
/// writes on an upload's record, the per-tenant layout, and the content and removal of a finished upload the
/// application asks for. The endpoint's responses in detail are the subject of its unit tests, not of this suite.
/// </summary>
/// <param name="createBackend">
/// Creates the backend of one scenario. The scenario owns it and disposes it when it ends.
/// </param>
public abstract class TusUploadLayerConformanceTestsBase(Func<ITusUploadLayerConformanceBackend> createBackend)
    : TestBase
{
    private const string _Route = "/files";
    private const string _Claims = "/claims";
    private const string _EndpointName = "documents";
    private const long _MaxSize = 1024 * 1024;
    private const string _UserHeader = "X-Test-User";
    private const string _TenantHeader = "X-Test-Tenant";

    private static readonly Caller _Alice = new("alice", Tenant: null);
    private static readonly Caller _Bob = new("bob", Tenant: null);
    private static readonly Caller _AliceAtAcme = new("alice", "acme");
    private static readonly Caller _AliceAtGlobex = new("alice", "globex");

    /// <summary>
    /// How long a request waits for the server to finish what it deferred until after the response. It only bounds
    /// a run in which that never happens, so that run fails instead of hanging.
    /// </summary>
    private static readonly TimeSpan _SignalTimeout = TimeSpan.FromSeconds(30);

    private readonly ITusUploadLayerConformanceBackend _backend = createBackend();
    private readonly SemaphoreSlim _responseCompleted = new(0);
    private WebApplication? _app;
    private HttpClient? _client;

    protected override async ValueTask DisposeAsyncCore()
    {
        _client?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _backend.DisposeAsync();
        _responseCompleted.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_answer_not_found_to_another_owner_and_leave_the_upload_untouched()
    {
        // given - alice's upload with its first 100 bytes stored
        await _StartHostAsync(scopeByTenant: false);
        var content = Faker.Random.Bytes(300);
        var location = await _CreateUploadAsync(_Alice, content.Length);
        await _WriteAsync(_Alice, location, content[..100], offset: 0);

        // when - bob names it, with a write the upload would take from alice
        // then - every request is answered as for an upload that does not exist
        await _ShouldNotReachAsync(_Bob, location, content[100..], offset: 100);

        // and the upload is as alice left it
        using var own = await _HeadAsync(_Alice, location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        _OffsetOf(own).Should().Be(100);
    }

    public virtual async Task should_give_the_application_the_creators_finished_upload_and_nothing_to_another_owner()
    {
        // given - alice's finished upload
        await _StartHostAsync(scopeByTenant: false);
        var content = Faker.Random.Bytes(300);
        var location = await _CreateUploadAsync(_Alice, content.Length);
        await _WriteAsync(_Alice, location, content, offset: 0);

        // when - a request of alice's names it, and then one of bob's
        using var own = await _ClaimAsync(_Alice, location);
        using var foreign = await _ClaimAsync(_Bob, location);

        // then - the application reads back what alice sent, and is given nothing for bob
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.Content.ReadAsByteArrayAsync(AbortToken)).Should().Equal(content);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant()
    {
        // given - a finished upload of the acme tenant
        await _StartHostAsync(scopeByTenant: true);
        var content = Faker.Random.Bytes(300);
        var location = await _CreateUploadAsync(_AliceAtAcme, content.Length);
        await _WriteAsync(_AliceAtAcme, location, content, offset: 0);

        // when - a request that runs in no tenant names it
        using var claim = await _ClaimAsync(_Alice, location);

        // then - it is answered like any upload that is not the caller's: the store, which refuses to look outside
        // a tenant, is never asked
        claim.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_refuse_a_clients_delete_of_a_finished_upload_and_leave_nothing_once_the_application_deletes_it()
    {
        // given - a finished upload of the acme tenant
        await _StartHostAsync(scopeByTenant: true);
        var content = Faker.Random.Bytes(300);
        var location = await _CreateUploadAsync(_AliceAtAcme, content.Length);
        await _WriteAsync(_AliceAtAcme, location, content, offset: 0);

        // when - its client asks to delete it
        using var clientDelete = await _SendAsync(_AliceAtAcme, HttpMethod.Delete, location);

        // then - the request is refused and the upload is whole
        clientDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var claim = await _ClaimAsync(_AliceAtAcme, location);
        (await claim.Content.ReadAsByteArrayAsync(AbortToken)).Should().Equal(content);

        // when - the application deletes the upload it found
        using var applicationDelete = await _SendAsync(_AliceAtAcme, HttpMethod.Delete, _ClaimUri(location));

        // then - the store holds nothing of the upload, in its tenant's folder or outside it
        applicationDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _backend.ListStoredNamesAsync(AbortToken)).Should().BeEmpty();

        using var head = await _HeadAsync(_AliceAtAcme, location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant()
    {
        // given - the same user starts an upload in each of two tenants, so only the tenant tells the two apart
        await _StartHostAsync(scopeByTenant: true);
        var acmeContent = Faker.Random.Bytes(300);
        var globexContent = Faker.Random.Bytes(400);
        var acme = await _CreateUploadAsync(_AliceAtAcme, acmeContent.Length);
        var globex = await _CreateUploadAsync(_AliceAtGlobex, globexContent.Length);
        await _WriteAsync(_AliceAtAcme, acme, acmeContent[..100], offset: 0);
        await _WriteAsync(_AliceAtGlobex, globex, globexContent[..100], offset: 0);

        // when - each tenant names the other's upload, with a write the upload would take inside its own tenant
        // then - neither reaches it
        await _ShouldNotReachAsync(_AliceAtGlobex, acme, acmeContent[100..], offset: 100);
        await _ShouldNotReachAsync(_AliceAtAcme, globex, globexContent[100..], offset: 100);

        // and each upload finishes inside its own tenant
        await _WriteAsync(_AliceAtAcme, acme, acmeContent[100..], offset: 100);
        await _WriteAsync(_AliceAtGlobex, globex, globexContent[100..], offset: 100);

        using var acmeHead = await _HeadAsync(_AliceAtAcme, acme);
        using var globexHead = await _HeadAsync(_AliceAtGlobex, globex);
        _OffsetOf(acmeHead).Should().Be(300);
        _OffsetOf(globexHead).Should().Be(400);

        // and the application is given each finished upload only inside its own tenant
        using var acmeFromGlobex = await _ClaimAsync(_AliceAtGlobex, acme);
        using var globexFromAcme = await _ClaimAsync(_AliceAtAcme, globex);
        using var acmeOwn = await _ClaimAsync(_AliceAtAcme, acme);
        using var globexOwn = await _ClaimAsync(_AliceAtGlobex, globex);
        acmeFromGlobex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        globexFromAcme.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await acmeOwn.Content.ReadAsByteArrayAsync(AbortToken)).Should().Equal(acmeContent);
        (await globexOwn.Content.ReadAsByteArrayAsync(AbortToken)).Should().Equal(globexContent);

        // and the store keeps everything it holds of an upload under that upload's tenant
        var names = await _backend.ListStoredNamesAsync(AbortToken);
        _NamesOf(names, acme).Should().NotBeEmpty().And.OnlyContain(static name => name.Contains("/.tenants/acme/"));
        _NamesOf(names, globex)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(static name => name.Contains("/.tenants/globex/"));
    }

    public virtual async Task should_return_the_clients_metadata_unchanged_and_without_the_servers_entries()
    {
        // given - a non-ASCII value, a key without a value, and a mixed-case key, from a caller who has a tenant,
        // so the endpoint writes every entry it has beside them
        await _StartHostAsync(scopeByTenant: true);
        var metadata = $"FileName {"تقرير-2026.pdf".ToBase64()},is_draft,x-trace-id {"abc123".ToBase64()}";

        // when
        var location = await _CreateUploadAsync(_AliceAtAcme, uploadLength: 300, metadata);
        using var head = await _HeadAsync(_AliceAtAcme, location);

        // then - the client reads back exactly what it sent
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        _MetadataOf(head).Should().Be(metadata);

        // and the store's own record does carry the server's entries, which the endpoint left out of the answer
        var stored = await _ReadStoredMetadataAsync(_AliceAtAcme, location);
        stored.Should().Contain("headless-");
    }

    public virtual async Task should_keep_metadata_that_fills_the_stores_limit_until_the_upload_finishes()
    {
        // given - what the endpoint adds to this caller's metadata, measured on a first upload
        await _StartHostAsync(scopeByTenant: true);
        var probe = $"filename {"probe.bin".ToBase64()}";
        var probeLocation = await _CreateUploadAsync(_AliceAtAcme, uploadLength: 300, probe);
        var stored = await _ReadStoredMetadataAsync(_AliceAtAcme, probeLocation);
        var added = stored!.Length - probe.Length;
        added.Should().BePositive();

        // and metadata that, with those entries, is exactly as long as the store accepts
        var metadata = _CreateMetadata(_backend.MaxMetadataLength - added);
        var content = Faker.Random.Bytes(300);

        // when
        var location = await _CreateUploadAsync(_AliceAtAcme, content.Length, metadata);
        using var created = await _HeadAsync(_AliceAtAcme, location);

        // then
        _MetadataOf(created).Should().Be(metadata);

        // when - the upload finishes, and the store rewrites its record beside metadata that already fills it
        await _WriteAsync(_AliceAtAcme, location, content, offset: 0);
        using var finished = await _HeadAsync(_AliceAtAcme, location);

        // then - the metadata is still whole
        finished.StatusCode.Should().Be(HttpStatusCode.OK);
        _OffsetOf(finished).Should().Be(300);
        _MetadataOf(finished).Should().Be(metadata);
    }

    /// <summary>
    /// Starts the host of one scenario: the store registered the way an application registers it, one upload
    /// endpoint, an endpoint of the application's own that asks for a finished upload, and, when
    /// <paramref name="scopeByTenant"/> is set, the tenancy setup that scopes uploads by tenant.
    /// </summary>
    private async Task _StartHostAsync(bool scopeByTenant)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(LoggerProvider);

        // A real server on a port the operating system picks. It answers an exception the application does not
        // handle with 500, where the in-memory test server would rethrow it to the client.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _backend.ConfigureServices(builder.Services);
        builder.Services.AddScoped<TestCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(static provider => provider.GetRequiredService<TestCurrentUser>());

        builder.Services.AddHeadlessTus(tus =>
        {
            _backend.UseStore(tus);

            // The scenario's requests stay the only writers under the backend's storage root.
            tus.ConfigureExpiredUploadsCleanup(static options => options.Enabled = false);

            // A named endpoint, so an upload's record carries every entry the endpoint can write.
            tus.AddUploadEndpoint(_EndpointName, static options => options.MaxSize = _MaxSize);
        });

        if (scopeByTenant)
        {
            builder.AddHeadlessTenancy(static tenancy => tenancy.Tus(static tus => tus.ScopeByTenant()));
        }

        var app = builder.Build();
        _app = app;
        app.Use(_SignalWhenTheResponseCompletes);
        app.Use(_SignInAsync);
        app.MapHeadlessTus(_Route, _EndpointName);
        app.MapGet(_Claims + "/{fileId}", _FindAsync);
        app.MapDelete(_Claims + "/{fileId}", _FindAndDeleteAsync);

        // The deploy step an application runs before its hosts start: a store does not create its own storage.
        await app.Services.ProvisionResourcesAsync(AbortToken);
        await app.StartAsync(AbortToken);

        _client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    /// <summary>
    /// Releases <see cref="_responseCompleted"/> after everything the endpoint does once a response is sent. The
    /// server runs those callbacks last-registered first, and this one is registered before the endpoint runs.
    /// </summary>
    private Task _SignalWhenTheResponseCompletes(HttpContext context, RequestDelegate next)
    {
        context.Response.OnCompleted(() =>
        {
            _responseCompleted.Release();

            return Task.CompletedTask;
        });

        return next(context);
    }

    /// <summary>
    /// The application's endpoint: it asks for the upload the request names and answers with its content.
    /// </summary>
    private static async Task _FindAsync(HttpContext context)
    {
        var upload = await _FindForRequestAsync(context);

        if (upload is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        await using var content = await upload.OpenContentAsync(context.RequestAborted);
        await content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    /// <summary>The application's endpoint that deletes the upload the request names.</summary>
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

        return uploads.FindAsync(fileId, _EndpointName, context.RequestAborted);
    }

    /// <summary>
    /// Stands in for the application's authentication and tenant resolution: the request's user and tenant are
    /// the ones its test headers name, entered through the same services an application uses.
    /// </summary>
    private static async Task _SignInAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers.TryGetValue(_UserHeader, out var user))
        {
            var currentUser = context.RequestServices.GetRequiredService<TestCurrentUser>();
            currentUser.IsAuthenticated = true;
            currentUser.UserId = new UserId(user.ToString());
        }

        if (!context.Request.Headers.TryGetValue(_TenantHeader, out var tenant))
        {
            await next(context);

            return;
        }

        using (context.RequestServices.GetRequiredService<ICurrentTenant>().Change(tenant.ToString()))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Sends a tus request as <paramref name="caller"/> and returns once the server finished everything it does
    /// for it, which includes what an endpoint defers until after the response.
    /// </summary>
    private async Task<HttpResponseMessage> _SendAsync(
        Caller caller,
        HttpMethod method,
        string uri,
        Action<HttpRequestMessage>? configure = null
    )
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add(_UserHeader, caller.User);

        if (caller.Tenant is not null)
        {
            request.Headers.Add(_TenantHeader, caller.Tenant);
        }

        configure?.Invoke(request);

        var response = await _client!.SendAsync(request, AbortToken);

        if (!await _responseCompleted.WaitAsync(_SignalTimeout, AbortToken))
        {
            response.Dispose();

            throw new TimeoutException(
                $"The server did not finish the {method} request to {uri} within {_SignalTimeout}."
            );
        }

        return response;
    }

    /// <summary>Creates an upload that must succeed and returns its location.</summary>
    private async Task<string> _CreateUploadAsync(Caller caller, long uploadLength, string? metadata = null)
    {
        using var response = await _SendAsync(
            caller,
            HttpMethod.Post,
            _Route,
            request =>
            {
                request.Headers.Add("Upload-Length", uploadLength.ToString(CultureInfo.InvariantCulture));

                if (metadata is not null)
                {
                    request.Headers.Add("Upload-Metadata", metadata);
                }
            }
        );

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    private Task<HttpResponseMessage> _PatchAsync(Caller caller, string location, byte[] body, long offset)
    {
        return _SendAsync(
            caller,
            HttpMethod.Patch,
            location,
            request =>
            {
                request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            }
        );
    }

    /// <summary>Sends a write that the upload must accept.</summary>
    private async Task _WriteAsync(Caller caller, string location, byte[] body, long offset)
    {
        using var response = await _PatchAsync(caller, location, body, offset);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private Task<HttpResponseMessage> _HeadAsync(Caller caller, string location)
    {
        return _SendAsync(caller, HttpMethod.Head, location);
    }

    /// <summary>Asks the application's own endpoint, as <paramref name="caller"/>, for the upload at a location.</summary>
    private Task<HttpResponseMessage> _ClaimAsync(Caller caller, string location)
    {
        return _SendAsync(caller, HttpMethod.Get, _ClaimUri(location));
    }

    private static string _ClaimUri(string location)
    {
        return $"{_Claims}/{_FileIdOf(location)}";
    }

    /// <summary>
    /// Sends a status request, the write <paramref name="body"/> at <paramref name="offset"/>, and a delete as
    /// <paramref name="caller"/>, and requires each to be answered as for an upload that does not exist.
    /// </summary>
    private async Task _ShouldNotReachAsync(Caller caller, string location, byte[] body, long offset)
    {
        using var head = await _HeadAsync(caller, location);
        using var patch = await _PatchAsync(caller, location, body, offset);
        using var delete = await _SendAsync(caller, HttpMethod.Delete, location);

        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Reads an upload's metadata as the store keeps it, through the host's store and inside the caller's tenant,
    /// the way an application's own code reads an upload outside a request.
    /// </summary>
    private async Task<string?> _ReadStoredMetadataAsync(Caller caller, string location)
    {
        var services = _app!.Services;
        var store = (ITusCreationStore)services.GetRequiredService<ITusStore>();

        using (services.GetRequiredService<ICurrentTenant>().Change(caller.Tenant))
        {
            return await store.GetUploadMetadataAsync(_FileIdOf(location), AbortToken);
        }
    }

    /// <summary>
    /// Builds an <c>Upload-Metadata</c> string of exactly <paramref name="length"/> characters: a file name and
    /// one long entry.
    /// </summary>
    private string _CreateMetadata(int length)
    {
        var fileName = $"filename {"report.pdf".ToBase64()}";

        // The long entry is a key, a space, and a base64 value. Base64 without padding comes in groups of four
        // characters, so the key takes up whatever the groups cannot.
        var entryLength = length - fileName.Length - 1;
        var key = "note" + new string('x', (entryLength - 1) % 4);
        var value = Convert.ToBase64String(Faker.Random.Bytes((entryLength - 1 - key.Length) / 4 * 3));
        var metadata = $"{fileName},{key} {value}";

        metadata.Should().HaveLength(length);

        return metadata;
    }

    /// <summary>The file id is the last segment of the location a creating request answers with.</summary>
    private static string _FileIdOf(string location)
    {
        return location[(location.LastIndexOf('/') + 1)..];
    }

    /// <summary>The stored names that belong to the upload at <paramref name="location"/>.</summary>
    private static IEnumerable<string> _NamesOf(IReadOnlyList<string> names, string location)
    {
        var fileId = _FileIdOf(location);

        return names.Where(name => name.Contains(fileId, StringComparison.Ordinal));
    }

    private static long _OffsetOf(HttpResponseMessage response)
    {
        return long.Parse(response.Headers.GetValues("Upload-Offset").Single(), CultureInfo.InvariantCulture);
    }

    private static string _MetadataOf(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Upload-Metadata").Single();
    }

    /// <summary>Who a request is sent as: the signed-in user, and the tenant it runs in when it has one.</summary>
    private sealed record Caller(string User, string? Tenant);
}
