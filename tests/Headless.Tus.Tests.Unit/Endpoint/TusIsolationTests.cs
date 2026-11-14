// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusIsolationTests : TestBase
{
    private const string _Route = TusEndpointTestHost.Route;

    private static TusEndpointTestHostSetup _WithTenancy()
    {
        return new TusEndpointTestHostSetup { RegisterCurrentTenant = true };
    }

    private static TusEndpointTestHostSetup _OwnedBy(Func<HeadlessTusUploadEndpointBuilder, object> ownedBy)
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
                ownedBy(
                    tus.UseStore(store)
                        .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize)
                ),
        };
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task should_answer_another_owners_request_exactly_like_an_unknown_id(string method)
    {
        // given - alice's upload, and the same store behind a bare tusdotnet endpoint: its answer for an id that
        // was never created is the reference the layer's refusal must match
        var setup = new TusEndpointTestHostSetup
        {
            Map = static endpoints =>
            {
                endpoints.MapHeadlessTus(_Route);

                var store = endpoints.ServiceProvider.GetRequiredService<ITusStore>();
                endpoints.MapTus("/raw", _ => Task.FromResult(new DefaultTusConfiguration { Store = store }));
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);
        var unknownId = Guid.NewGuid().ToString("n");

        // when
        host.User = "bob";
        using var foreign = await _SendAsync(host, method, location);
        using var unknown = await _SendAsync(host, method, $"{_Route}/{unknownId}");
        using var reference = await _SendAsync(host, method, $"/raw/{unknownId}");

        // then
        reference.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var expected = await _DescribeAsync(reference);
        (await _DescribeAsync(foreign)).Should().Be(expected);
        (await _DescribeAsync(unknown)).Should().Be(expected);

        host.User = TusEndpointTestHost.DefaultUser;
        using var own = await host.HeadAsync(location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        own.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Theory]
    [InlineData("/videos", "/avatars")]
    [InlineData("/videos", "/files")]
    [InlineData("/files", "/videos")]
    public async Task should_answer_404_when_an_upload_is_reached_through_another_endpoints_path(
        string createdOn,
        string reachedThrough
    )
    {
        // given - the default endpoint and two named ones over one store
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
            {
                tus.UseStore(store);
                tus.AddUploadEndpoint(static options => options.MaxSize = 1000);
                tus.AddUploadEndpoint("videos", static options => options.MaxSize = 1000);
                tus.AddUploadEndpoint("avatars", static options => options.MaxSize = 1000);
            },
            Map = static endpoints =>
            {
                endpoints.MapHeadlessTus("/files");
                endpoints.MapHeadlessTus("/videos", "videos");
                endpoints.MapHeadlessTus("/avatars", "avatars");
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10, route: createdOn);
        var elsewhere = $"{reachedThrough}/{TusEndpointTestHost.FileIdOf(location)}";
        var unknown = $"{reachedThrough}/{Guid.NewGuid():n}";

        // when
        using var head = await host.HeadAsync(elsewhere);
        using var patch = await host.PatchAsync(elsewhere, new byte[10], offset: 0);
        using var delete = await host.DeleteAsync(elsewhere);
        using var unknownHead = await host.HeadAsync(unknown);
        using var unknownPatch = await host.PatchAsync(unknown, new byte[10], offset: 0);
        using var unknownDelete = await host.DeleteAsync(unknown);

        // then - the other endpoint answers as if the upload did not exist
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _DescribeAsync(head)).Should().Be(await _DescribeAsync(unknownHead));
        (await _DescribeAsync(patch)).Should().Be(await _DescribeAsync(unknownPatch));
        (await _DescribeAsync(delete)).Should().Be(await _DescribeAsync(unknownDelete));

        using var own = await host.HeadAsync(location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        own.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Theory]
    [InlineData("acme", "globex")]
    [InlineData("acme", null)]
    [InlineData(null, "acme")]
    public async Task should_answer_404_when_the_callers_tenant_is_not_the_uploads(
        string? uploadTenant,
        string? callerTenant
    )
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_WithTenancy(), AbortToken);
        host.Tenant = uploadTenant;
        var location = await host.CreateUploadAsync(uploadLength: 10);
        var unknown = $"{_Route}/{Guid.NewGuid():n}";

        // when - the same user, in another tenant or in none
        host.Tenant = callerTenant;
        using var head = await host.HeadAsync(location);
        using var patch = await host.PatchAsync(location, new byte[10], offset: 0);
        using var delete = await host.DeleteAsync(location);
        using var unknownHead = await host.HeadAsync(unknown);
        using var unknownPatch = await host.PatchAsync(unknown, new byte[10], offset: 0);
        using var unknownDelete = await host.DeleteAsync(unknown);

        // then - the caller's tenant is answered as if the upload did not exist
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _DescribeAsync(head)).Should().Be(await _DescribeAsync(unknownHead));
        (await _DescribeAsync(patch)).Should().Be(await _DescribeAsync(unknownPatch));
        (await _DescribeAsync(delete)).Should().Be(await _DescribeAsync(unknownDelete));

        host.Tenant = uploadTenant;
        using var own = await host.HeadAsync(location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        own.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task should_return_the_clients_metadata_without_what_the_server_recorded()
    {
        // given
        const string metadata = "filename cmVwb3J0LnBkZg==,filetype YXBwbGljYXRpb24vcGRm,draft";
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10, metadata);

        // when
        using var head = await host.HeadAsync(location);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Metadata").Should().ContainSingle().Which.Should().Be(metadata);
    }

    [Fact]
    public async Task should_return_no_metadata_when_the_client_sent_none()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // when
        using var head = await host.HeadAsync(location);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.Contains("Upload-Metadata").Should().BeFalse();
    }

    [Fact]
    public async Task should_record_the_endpoint_the_owner_and_the_tenant_on_the_upload()
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            RegisterCurrentTenant = true,
            ConfigureTus = static (tus, store) =>
                tus.UseStore(store).AddUploadEndpoint("videos", static options => options.MaxSize = 1000),
            Map = static endpoints => endpoints.MapHeadlessTus(_Route, "videos"),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        host.Tenant = "acme";

        // when
        var location = await host.CreateUploadAsync(uploadLength: 10, "filename cmVwb3J0LnBkZg==");

        // then - what the store keeps, read without the layer
        var stored = await _StoredMetadataAsync(host, location);
        stored
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["filename"] = "report.pdf",
                    ["headless-endpoint"] = "videos",
                    ["headless-owner"] = "alice",
                    ["headless-tenant"] = "acme",
                }
            );
    }

    [Fact]
    public async Task should_record_no_tenant_when_the_request_has_none()
    {
        // given - the default endpoint on a host with no tenancy
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // then
        var stored = await _StoredMetadataAsync(host, location);
        stored.Should().ContainKey("headless-owner").WhoseValue.Should().Be("alice");
        stored.Should().NotContainKey("headless-tenant");
    }

    [Theory]
    [InlineData("headless-owner Ym9i")]
    [InlineData("filename cmVwb3J0LnBkZg==,headless-anything")]
    [InlineData("Headless-Tenant YWNtZQ==")]
    public async Task should_answer_400_when_the_client_sends_a_reserved_metadata_key(string metadata)
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, metadata);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType.Should().Be(new MediaTypeHeaderValue("text/plain"));
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("headless-");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task should_answer_401_when_the_request_has_no_owner(string method)
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // when - nobody is signed in; a POST creates a second upload and the others name the first
        host.User = null;
        using var response = await _SendAsync(host, method, location);

        // then - refused, with the first upload still the only one
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Directory.EnumerateFiles(host.Directory, "*.metadata").Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task should_answer_401_when_the_owner_function_returns_a_blank_key(string ownerKey)
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(
            _OwnedBy(endpoint => endpoint.OwnedBy(_ => ownerKey)),
            AbortToken
        );

        // when
        using var response = await host.CreateAsync(uploadLength: 10);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_options_when_the_request_has_no_owner()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);

        // when - a capability query, which browsers also send without credentials before a cross-origin request
        using var request = new HttpRequestMessage(HttpMethod.Options, _Route);
        using var response = await host.Client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Contains("Tus-Extension").Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_400_when_the_owner_key_is_longer_than_256_characters()
    {
        // given - the owner key is the request's X-Owner header
        await using var host = await TusEndpointTestHost.StartAsync(
            _OwnedBy(endpoint => endpoint.OwnedBy(context => context.Request.Headers["X-Owner"].ToString())),
            AbortToken
        );

        // when
        using var atLimit = await host.CreateAsync(
            uploadLength: 10,
            configure: request => request.Headers.Add("X-Owner", new string('a', 256))
        );
        using var aboveLimit = await host.CreateAsync(
            uploadLength: 10,
            configure: request => request.Headers.Add("X-Owner", new string('a', 257))
        );

        // then
        atLimit.StatusCode.Should().Be(HttpStatusCode.Created);
        aboveLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.EnumerateFiles(host.Directory, "*.metadata").Should().ContainSingle();
    }

    [Fact]
    public async Task should_let_every_caller_with_the_owner_key_reach_the_upload()
    {
        // given - uploads belong to a workspace, not to the user who started them
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OwnedBy(static context => context.Request.RouteValues["workspace"]?.ToString()),
            Map = static endpoints => endpoints.MapHeadlessTus("/workspaces/{workspace}/files"),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10, route: "/workspaces/design/files");
        var fileId = TusEndpointTestHost.FileIdOf(location);

        // when - another member of the workspace, and the same user in another workspace
        host.User = "bob";
        using var sameWorkspace = await host.HeadAsync(location);
        host.User = TusEndpointTestHost.DefaultUser;
        using var otherWorkspace = await host.HeadAsync($"/workspaces/sales/files/{fileId}");

        // then
        sameWorkspace.StatusCode.Should().Be(HttpStatusCode.OK);
        otherWorkspace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _StoredMetadataAsync(host, location))
            .Should()
            .ContainKey("headless-owner")
            .WhoseValue.Should()
            .Be("design");
    }

    [Theory]
    [InlineData("alice")]
    [InlineData(" Alice")]
    [InlineData("Alice ")]
    public async Task should_answer_404_when_the_owner_key_differs_from_the_uploads_only_in_case_or_white_space(
        string otherKey
    )
    {
        // given - the owner key is the request's owner query value, which keeps its case and white space as sent
        await using var host = await TusEndpointTestHost.StartAsync(
            _OwnedBy(endpoint => endpoint.OwnedBy(static context => context.Request.Query["owner"].ToString())),
            AbortToken
        );
        var location = await host.CreateUploadAsync(uploadLength: 10, route: $"{_Route}?owner=Alice");

        // when
        using var sameKey = await host.HeadAsync($"{location}?owner=Alice");
        using var lookalike = await host.HeadAsync($"{location}?owner={Uri.EscapeDataString(otherKey)}");

        // then - owner keys are compared exactly, so a key that only looks the same reaches nothing
        sameKey.StatusCode.Should().Be(HttpStatusCode.OK);
        lookalike.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_not_run_the_completion_handler_of_the_endpoint_an_upload_is_reached_through()
    {
        // given - the default endpoint without a completion handler, and a videos endpoint with one, over one store
        var recorder = new RecordingHandler();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
            {
                tus.UseStore(store);
                tus.AddUploadEndpoint(static options => options.MaxSize = 1000);
                tus.AddUploadEndpoint("videos", static options => options.MaxSize = 1000)
                    .OnCompleted<RecordingHandler>();
            },
            ConfigureServices = services => services.AddSingleton(recorder),
            Map = static endpoints =>
            {
                endpoints.MapHeadlessTus("/files");
                endpoints.MapHeadlessTus("/videos", "videos");
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10, route: "/files");

        // when - every byte of the default endpoint's upload is sent through the videos endpoint's path
        using var patch = await host.PatchAsync(
            $"/videos/{TusEndpointTestHost.FileIdOf(location)}",
            new byte[10],
            offset: 0
        );

        // then - nothing was written, so the upload is not complete and the videos handler was never given it
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        recorder.Uploads.Should().BeEmpty();

        using var own = await host.HeadAsync(location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        own.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");

        // and the videos handler does run for an upload of its own endpoint
        var video = await host.CreateUploadAsync(uploadLength: 10, route: "/videos");
        using var videoPatch = await host.PatchAsync(video, new byte[10], offset: 0);
        videoPatch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        recorder.Uploads.Should().ContainSingle().Which.FileId.Should().Be(TusEndpointTestHost.FileIdOf(video));
    }

    [Fact]
    public async Task should_await_an_asynchronous_owner_function()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(
            _OwnedBy(endpoint =>
                endpoint.OwnedBy(static async context =>
                {
                    await Task.Yield();

                    return context.Request.Headers["X-Owner"].ToString();
                })
            ),
            AbortToken
        );

        // when
        using var created = await host.CreateAsync(
            uploadLength: 10,
            configure: static request => request.Headers.Add("X-Owner", "design")
        );
        var location = created.Headers.Location!.ToString();
        using var sameOwner = await host.SendAsync(
            HttpMethod.Head,
            location,
            static request => request.Headers.Add("X-Owner", "design")
        );
        using var otherOwner = await host.SendAsync(
            HttpMethod.Head,
            location,
            static request => request.Headers.Add("X-Owner", "sales")
        );

        // then
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        sameOwner.StatusCode.Should().Be(HttpStatusCode.OK);
        otherOwner.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_answer_404_for_a_malformed_file_id()
    {
        // given - the disk store throws for an id that is not one of its own
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var malformed = $"{_Route}/not-a-file-id";

        // when
        using var head = await host.HeadAsync(malformed);
        using var patch = await host.PatchAsync(malformed, new byte[10], offset: 0);
        using var delete = await host.DeleteAsync(malformed);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_authorize_a_post_that_overrides_its_method_to_delete_as_a_delete()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // when - a POST would be a create, which any signed-in caller may send
        host.User = "bob";
        using var foreign = await _PostAsDeleteAsync(host, location);
        using var unknown = await _PostAsDeleteAsync(host, $"{_Route}/{Guid.NewGuid():n}");

        // then
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _DescribeAsync(foreign)).Should().Be(await _DescribeAsync(unknown));
        Directory.EnumerateFiles(host.Directory, "*.metadata").Should().ContainSingle();

        // when
        host.User = TusEndpointTestHost.DefaultUser;
        using var own = await _PostAsDeleteAsync(host, location);

        // then
        own.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_404_for_an_upload_created_without_the_endpoint()
    {
        // given - an upload written straight to the store, so nothing recorded whose it is
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var fileId = await host.Store.CreateFileAsync(10, "filename cmVwb3J0LnBkZg==", AbortToken);
        var location = $"{_Route}/{fileId}";
        var unknown = $"{_Route}/{Guid.NewGuid():n}";

        // when
        using var head = await host.HeadAsync(location);
        using var patch = await host.PatchAsync(location, new byte[10], offset: 0);
        using var delete = await host.DeleteAsync(location);
        using var unknownHead = await host.HeadAsync(unknown);
        using var unknownPatch = await host.PatchAsync(unknown, new byte[10], offset: 0);
        using var unknownDelete = await host.DeleteAsync(unknown);

        // then - answered as if the upload did not exist
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _DescribeAsync(head)).Should().Be(await _DescribeAsync(unknownHead));
        (await _DescribeAsync(patch)).Should().Be(await _DescribeAsync(unknownPatch));
        (await _DescribeAsync(delete)).Should().Be(await _DescribeAsync(unknownDelete));
        (await host.Store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
    }

    [Theory]
    [InlineData("headless-owner")]
    [InlineData("headless-owner ***")]
    [InlineData("headless-owner YWxpY2U=,headless-tenant")]
    [InlineData("headless-owner YWxpY2U=,headless-tenant ***")]
    [InlineData("headless-owner YWxpY2U=,headless-endpoint")]
    public async Task should_answer_404_when_part_of_the_uploads_stamp_cannot_be_read(string storedMetadata)
    {
        // given - alice, with no tenant, on the default endpoint: the caller a stamp naming only alice belongs to
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var readable = await host.Store.CreateFileAsync(10, "headless-owner YWxpY2U=", AbortToken);
        var unreadable = await host.Store.CreateFileAsync(10, storedMetadata, AbortToken);

        // when
        using var readableHead = await host.HeadAsync($"{_Route}/{readable}");
        using var unreadableHead = await host.HeadAsync($"{_Route}/{unreadable}");

        // then - an entry that cannot be read is not taken as "no tenant" or "the default endpoint"
        readableHead.StatusCode.Should().Be(HttpStatusCode.OK);
        unreadableHead.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_answer_503_to_a_write_and_423_to_a_status_request_when_the_store_fails_during_the_lookup()
    {
        // given
        FailingMetadataStore? failing = null;
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
                tus.UseStore(failing = new FailingMetadataStore(store))
                    .AddUploadEndpoint(static options => options.MaxSize = 1000),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);

        // when - the upload exists and is the caller's, but the store cannot be read
        failing!.Fails = true;
        using var patch = await host.PatchAsync(location, new byte[10], offset: 0);
        using var head = await host.HeadAsync(location);

        // then - refusals the client retries, where "not found" would make it start the upload again. A tus
        // client retries a failed write after any server error, and asks for an upload's status again only
        // after 423
        patch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        head.StatusCode.Should().Be(HttpStatusCode.Locked);
        head.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");

        // when
        failing.Fails = false;
        using var headAgain = await host.HeadAsync(location);
        using var retried = await host.PatchAsync(location, new byte[10], offset: 0);

        // then
        headAgain.StatusCode.Should().Be(HttpStatusCode.OK);
        retried.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_complete_an_upload_whose_bytes_arrive_with_the_creating_request()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(setup: null, AbortToken);
        var content = "hello world"u8.ToArray();

        // when - one request both creates the upload and writes to it, so it is authorized as each in turn
        using var created = await host.CreateAsync(
            content.Length,
            configure: request =>
            {
                request.Content = new ByteArrayContent(content);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            }
        );

        // then
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
    }

    [Fact]
    public async Task should_offer_only_the_extensions_the_registered_store_supports()
    {
        // given - a store that can create uploads and nothing more
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(new CreationOnlyTusStore(store))
                    .AddUploadEndpoint(static options => options.MaxSize = 100),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var request = new HttpRequestMessage(HttpMethod.Options, _Route);
        using var options = await host.Client.SendAsync(request, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: 10);
        using var delete = await host.DeleteAsync(location);
        using var deferred = await host.CreateAsync(uploadLength: null);

        // then
        options
            .Headers.GetValues("Tus-Extension")
            .Should()
            .ContainSingle()
            .Which.Split(',')
            .Should()
            .BeEquivalentTo("creation", "creation-with-upload");
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deferred.StatusCode.Should().NotBe(HttpStatusCode.Created);
        Directory.EnumerateFiles(host.Directory, "*.metadata").Should().ContainSingle();
    }

    [Fact]
    public async Task should_write_through_a_store_that_reads_only_from_a_stream()
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(new CreationOnlyTusStore(store))
                    .AddUploadEndpoint(static options => options.MaxSize = 100),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var content = "hello world"u8.ToArray();
        var location = await host.CreateUploadAsync(content.Length);

        // when
        using var first = await host.PatchAsync(location, content[..5], offset: 0);
        using var second = await host.PatchAsync(location, content[5..], offset: 5);
        using var tooMuch = await host.PatchAsync(location, new byte[1], offset: 11);

        // then
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        tooMuch.StatusCode.Should().NotBe(HttpStatusCode.NoContent);

        var file = await host.Store.GetFileAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);
        await using var stored = await file.GetContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_throw_at_mapping_time_when_nothing_can_name_the_owner()
    {
        // given - no ICurrentUser in the container and no owner function on the endpoint
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHeadlessTus(tus =>
        {
            tus.UseStore(new TusDiskStore(Path.GetTempPath()));
            tus.AddUploadEndpoint(options => options.MaxSize = 100);
            tus.AddUploadEndpoint("videos", options => options.MaxSize = 100);
            tus.AddUploadEndpoint("shared", options => options.MaxSize = 100).OwnedBy(_ => "everyone");
        });
        await using var app = builder.Build();

        // when
        var mapDefault = () => app.MapHeadlessTus("/files");
        var mapNamed = () => app.MapHeadlessTus("/videos", "videos");
        var mapOwned = () => app.MapHeadlessTus("/shared", "shared");

        // then
        mapDefault
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "The default upload endpoint has no owner for its uploads. Call OwnedBy(…) on the endpoint, or "
                    + "register ICurrentUser so that the signed-in user owns them."
            );
        mapNamed
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "The upload endpoint \"videos\" has no owner for its uploads. Call OwnedBy(…) on the endpoint, or "
                    + "register ICurrentUser so that the signed-in user owns them."
            );
        mapOwned.Should().NotThrow();
    }

    private static Task<HttpResponseMessage> _SendAsync(TusEndpointTestHost host, string method, string location)
    {
        return method switch
        {
            "POST" => host.CreateAsync(uploadLength: 10),
            "HEAD" => host.HeadAsync(location),
            "PATCH" => host.PatchAsync(location, new byte[10], offset: 0),
            "DELETE" => host.DeleteAsync(location),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, message: null),
        };
    }

    private static Task<HttpResponseMessage> _PostAsDeleteAsync(TusEndpointTestHost host, string location)
    {
        return host.SendAsync(
            HttpMethod.Post,
            location,
            static request => request.Headers.Add("X-HTTP-Method-Override", "DELETE")
        );
    }

    private static Task<string> _DescribeAsync(HttpResponseMessage response)
    {
        return TusEndpointTestHost.DescribeAsync(response, AbortToken);
    }

    /// <summary>The metadata the store itself keeps for an upload, decoded.</summary>
    private static async Task<Dictionary<string, string>> _StoredMetadataAsync(
        TusEndpointTestHost host,
        string location
    )
    {
        var raw = await host.Store.GetUploadMetadataAsync(TusEndpointTestHost.FileIdOf(location), AbortToken);

        return TusEndpointTestHost.DecodeMetadata(raw);
    }

    /// <summary>A completion handler that keeps every upload and records each one it was given.</summary>
    private sealed class RecordingHandler : ITusUploadCompletedHandler
    {
        public ConcurrentQueue<TusCompletedUpload> Uploads { get; } = new();

        public ValueTask<TusUploadHandoff> HandleAsync(TusCompletedUpload upload, CancellationToken cancellationToken)
        {
            Uploads.Enqueue(upload);

            return ValueTask.FromResult(TusUploadHandoff.Keep);
        }
    }

    /// <summary>A store whose metadata read fails, on demand, the way an unreachable backend does.</summary>
    private sealed class FailingMetadataStore(TusDiskStore inner) : CreationOnlyTusStore(inner)
    {
        public bool Fails { get; set; }

        public override Task<string> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            return Fails
                ? Task.FromException<string>(new IOException("The store is unreachable."))
                : base.GetUploadMetadataAsync(fileId, cancellationToken);
        }
    }
}
