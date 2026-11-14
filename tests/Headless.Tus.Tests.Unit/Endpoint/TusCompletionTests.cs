// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Headless.Context;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusCompletionTests : TestBase
{
    private static readonly byte[] _Content = "hello world"u8.ToArray();

    /// <summary>
    /// How long a scenario waits for a signal the host must raise. It only bounds a run in which the signal never
    /// comes, so that run fails instead of hanging.
    /// </summary>
    private static readonly TimeSpan _SignalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A host whose default endpoint hands its finished uploads to a handler that does what
    /// <paramref name="script"/> says, over a store that records handed-off uploads.
    /// </summary>
    private static TusEndpointTestHostSetup _Handled(
        HandlerScript script,
        Action<HandoffDiskStore>? useStore = null,
        Action<IApplicationBuilder>? middleware = null,
        bool useKestrel = false
    )
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, disk) =>
            {
                var store = new HandoffDiskStore(disk);
                useStore?.Invoke(store);

                tus.UseStore(store)
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowEmptyUploads = true;
                    })
                    .OnCompleted<ScriptedHandler>();
            },
            ConfigureServices = services => services.AddSingleton(script),
            ConfigureMiddleware = middleware,
            UseKestrel = useKestrel,
        };
    }

    [Fact]
    public async Task should_give_the_handler_the_finished_upload_and_its_content()
    {
        // given
        byte[]? read = null;
        var script = new HandlerScript
        {
            Run = async (upload, cancellationToken) =>
            {
                await using var content = await upload.OpenContentAsync(cancellationToken);
                await using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                read = buffer.ToArray();

                return TusUploadHandoff.Keep;
            },
        };
        var setup = new TusEndpointTestHostSetup
        {
            RegisterCurrentTenant = true,
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(new HandoffDiskStore(disk))
                    .AddUploadEndpoint("videos", static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services => services.AddSingleton(script),
            Map = static endpoints => endpoints.MapHeadlessTus("/videos", "videos"),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        host.Tenant = "acme";
        var metadata = $"filename {Convert.ToBase64String("report.pdf"u8)},draft";
        var location = await host.CreateUploadAsync(_Content.Length, metadata, "/videos");

        // when - the upload arrives in two writes
        using var first = await host.PatchAsync(location, _Content[..4], offset: 0);
        script.Uploads.Should().BeEmpty("the upload still lacks bytes");
        using var last = await host.PatchAsync(location, _Content[4..], offset: 4);

        // then
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        last.StatusCode.Should().Be(HttpStatusCode.NoContent);
        last.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");

        var upload = script.Uploads.Should().ContainSingle().Which;
        upload.FileId.Should().Be(TusEndpointTestHost.FileIdOf(location));
        upload.Length.Should().Be(11);
        upload
            .Metadata.Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["filename"] = "report.pdf", ["draft"] = "" });
        upload.TenantId.Should().Be("acme");
        upload.OwnerKey.Should().Be(TusEndpointTestHost.DefaultUser);
        read.Should().Equal(_Content);

        // the handler is built by the request's services, so it sees the request's signed-in user
        script.ResolvedAs.Should().Equal(TusEndpointTestHost.DefaultUser);
    }

    [Fact]
    public async Task should_not_run_the_handler_again_once_it_took_the_upload_over()
    {
        // given
        var script = new HandlerScript();
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var incomplete = await host.HeadAsync(location);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        using var head = await host.HeadAsync(location);

        // then
        incomplete.StatusCode.Should().Be(HttpStatusCode.OK);
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task should_run_the_handler_again_on_a_status_request_after_it_threw_on_the_final_write()
    {
        // given - a real server, which answers an exception the application does not handle with 500
        var script = new HandlerScript();
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // then - the bytes are stored and the client is told the request failed
        patch.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        script.Uploads.Should().ContainSingle();

        // when - the client asks where the upload stands, as it does after a failed write
        using var head = await host.HeadAsync(location);

        // then - the handler ran again and the client learns the upload is complete
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().HaveCount(2);

        // when
        using var again = await host.HeadAsync(location);

        // then
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        script.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_answer_423_when_the_handler_throws_again_on_a_status_request()
    {
        // given
        var script = new HandlerScript();
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        script.Then(static (_, _) => throw new IOException("The queue is still unreachable."));
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        using var head = await host.HeadAsync(location);

        // then - the one status a tus client answers by asking the same upload again
        head.StatusCode.Should().Be(HttpStatusCode.Locked);
        head.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
        script.Uploads.Should().HaveCount(2);

        // when
        using var retried = await host.HeadAsync(location);

        // then
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        retried.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_run_the_handler_on_the_next_status_request_when_the_request_died_before_it_ran()
    {
        // given - a store that holds the final write, once its bytes are stored, until the client is gone
        var script = new HandlerScript();
        var stored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var requestEnded = new SemaphoreSlim(0);
        var setup = _Handled(
            script,
            store =>
                store.AfterAppend = async cancellationToken =>
                {
                    stored.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                },
            app => _SignalWhenTheRequestEnds(app, HttpMethods.Patch, requestEnded)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when - the client disconnects after its last byte is stored and before tusdotnet raises completion
        var patch = host.PatchAsync(location, _Content, offset: 0, cancellationToken: abandon.Token);
        await stored.Task.WaitAsync(_SignalTimeout, AbortToken);
        await abandon.CancelAsync();
        await FluentActions.Awaiting(() => patch).Should().ThrowAsync<OperationCanceledException>();
        await _WaitAsync(requestEnded);

        // then
        script.Uploads.Should().BeEmpty();

        // when
        using var head = await host.HeadAsync(location);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task should_cancel_the_handler_when_the_client_abandons_the_request()
    {
        // given - a handler that waits for as long as its request lives
        var script = new HandlerScript();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var requestEnded = new SemaphoreSlim(0);
        script.Then(
            async (_, cancellationToken) =>
            {
                entered.SetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;

                    throw;
                }

                return TusUploadHandoff.Keep;
            }
        );
        var setup = _Handled(
            script,
            middleware: app => _SignalWhenTheRequestEnds(app, HttpMethods.Patch, requestEnded)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var patch = host.PatchAsync(location, _Content, offset: 0, cancellationToken: abandon.Token);
        await entered.Task.WaitAsync(_SignalTimeout, AbortToken);
        await abandon.CancelAsync();
        await FluentActions.Awaiting(() => patch).Should().ThrowAsync<OperationCanceledException>();
        await _WaitAsync(requestEnded);

        // then
        cancelled.Should().BeTrue();

        // when - the upload was not handed off, so the next status request runs the handler again
        using var head = await host.HeadAsync(location);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        script.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_delete_the_upload_after_the_response_when_the_handler_returns_delete()
    {
        // given
        using var responseCompleted = new SemaphoreSlim(0);
        HandoffDiskStore? store = null;
        var script = new HandlerScript { Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Delete) };
        var setup = new TusEndpointTestHostSetup
        {
            RegisterCurrentTenant = true,
            ConfigureTus = (tus, disk) =>
                tus.UseStore(store = new HandoffDiskStore(disk))
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services => services.AddSingleton(script),
            ConfigureMiddleware = app => TusEndpointTestHost.SignalWhenTheResponseCompletes(app, responseCompleted),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        host.Tenant = "acme";
        var location = await host.CreateUploadAsync(_Content.Length);
        await _WaitAsync(responseCompleted);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        await _WaitAsync(responseCompleted);

        // then - the write is answered as complete, and the upload is removed in the tenant it was created in
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        store!.DeleteTenants.Should().Equal("acme");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_the_upload_handed_off_when_deleting_it_after_the_response_fails()
    {
        // given - a handler that asks for the upload to be deleted, over a store that cannot delete it
        using var responseCompleted = new SemaphoreSlim(0);
        var script = new HandlerScript { Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Delete) };
        var setup = _Handled(
            script,
            static store => store.FailsDeletes = true,
            app => TusEndpointTestHost.SignalWhenTheResponseCompletes(app, responseCompleted)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        await _WaitAsync(responseCompleted);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        await _WaitAsync(responseCompleted);
        using var head = await host.HeadAsync(location);
        using var delete = await host.DeleteAsync(location);

        // then - the handler returned, so it is not owed again, and the upload stays the application's
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        script.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task should_answer_the_status_request_before_deleting_when_the_handler_returns_delete_on_it()
    {
        // given
        using var responseCompleted = new SemaphoreSlim(0);
        var script = new HandlerScript { Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Delete) };
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        var setup = _Handled(
            script,
            middleware: app => TusEndpointTestHost.SignalWhenTheResponseCompletes(app, responseCompleted),
            useKestrel: true
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        await _WaitAsync(responseCompleted);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        await _WaitAsync(responseCompleted);

        // when
        using var head = await host.HeadAsync(location);
        await _WaitAsync(responseCompleted);

        // then - a tus client that reads this answer reports the upload as done instead of sending it again
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().HaveCount(2);
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_400_with_the_reason_and_remove_the_upload_when_the_handler_rejects_on_the_final_write()
    {
        // given
        var script = new HandlerScript
        {
            Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Reject("The file holds a virus.")),
        };
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // then
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        patch.Content.Headers.ContentType.Should().Be(new MediaTypeHeaderValue("text/plain"));
        (await patch.Content.ReadAsStringAsync(AbortToken)).Should().Be("The file holds a virus.");
        patch.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
        patch.Headers.Contains("Upload-Offset").Should().BeFalse();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();

        // when
        using var head = await host.HeadAsync(location);

        // then
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        script.Uploads.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_answer_400_with_the_reason_when_the_handler_rejects_inside_the_creating_request(
        bool withData
    )
    {
        // given - an upload that is complete when its creating request ends: its bytes come with that request, or
        // it has none
        var script = new HandlerScript
        {
            Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Reject("The file holds a virus.")),
        };
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);

        // when
        using var created = await _CreateCompleteAsync(host, withData);

        // then
        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await created.Content.ReadAsStringAsync(AbortToken)).Should().Be("The file holds a virus.");
        created.Headers.Location.Should().BeNull();
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_delete_the_upload_and_fail_the_creating_request_when_the_handler_throws_inside_it(
        bool withData
    )
    {
        // given - a real server, which answers an exception the application does not handle with 500
        var script = new HandlerScript { Run = static (_, _) => throw new IOException("The queue is unreachable.") };
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);

        // when
        using var created = await _CreateCompleteAsync(host, withData);

        // then - the client never learns the upload's URL, so nothing could ever run the handler for it again
        created.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_the_upload_when_the_handler_kept_it_and_the_handoff_cannot_be_recorded_in_the_creating_request(
        bool withData
    )
    {
        // given - a handler that keeps the upload, over a store that cannot record the handoff
        var script = new HandlerScript();
        var setup = _Handled(script, static store => store.FailsMarks = true, useKestrel: true);
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var created = await _CreateCompleteAsync(host, withData);

        // then - the application already holds the upload, so the failed request must not take it away
        created.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var kept = script.Uploads.Should().ContainSingle().Which.FileId;
        (await host.Store.FileExistAsync(kept, AbortToken)).Should().BeTrue();
        (await host.Store.GetUploadOffsetAsync(kept, AbortToken)).Should().Be(withData ? _Content.Length : 0);
    }

    [Fact]
    public async Task should_delete_the_upload_when_the_handler_asked_for_that_and_the_handoff_cannot_be_recorded_in_the_creating_request()
    {
        // given
        var script = new HandlerScript { Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Delete) };
        var setup = _Handled(script, static store => store.FailsMarks = true, useKestrel: true);
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var created = await _CreateCompleteAsync(host, withData: true);

        // then - nobody holds the upload: the handler gave it up, and the client never learned its URL
        created.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_the_handler_again_on_a_status_request_when_the_handoff_could_not_be_recorded()
    {
        // given - a handler that keeps the upload, over a store that cannot record the handoff at first
        HandoffDiskStore? store = null;
        var script = new HandlerScript();
        var setup = _Handled(script, created => (store = created).FailsMarks = true, useKestrel: true);
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        using var head = await host.HeadAsync(location);

        // then - the write fails, and the status request answers the one status a tus client asks again after
        patch.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        head.StatusCode.Should().Be(HttpStatusCode.Locked);
        script.Uploads.Should().HaveCount(2);

        // when
        store!.FailsMarks = false;
        using var retried = await host.HeadAsync(location);
        using var delete = await host.DeleteAsync(location);

        // then - the upload stayed where it was and is handed off once the record is written
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        retried.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().HaveCount(3);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_let_the_client_delete_an_upload_whose_handoff_could_not_be_recorded()
    {
        // given - the handler kept the upload, and the store could not record that
        var script = new HandlerScript();
        var setup = _Handled(script, static store => store.FailsMarks = true, useKestrel: true);
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        using var delete = await host.DeleteAsync(location);

        // then - without the record the upload is still owed, and an owed upload is its uploader's to cancel
        patch.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_hand_off_an_upload_that_is_complete_when_its_creating_request_ends(bool withData)
    {
        // given
        var script = new HandlerScript();
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);

        // when
        using var created = await _CreateCompleteAsync(host, withData);
        using var delete = await host.DeleteAsync(created.Headers.Location!.ToString());

        // then
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        script.Uploads.Should().ContainSingle().Which.Length.Should().Be(withData ? _Content.Length : 0);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_answer_400_and_remove_the_upload_when_the_handler_rejects_on_a_status_request()
    {
        // given
        var script = new HandlerScript
        {
            Run = static (_, _) => ValueTask.FromResult(TusUploadHandoff.Reject("The file holds a virus.")),
        };
        script.Then(static (_, _) => throw new IOException("The scanner is unreachable."));
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        using var head = await host.HeadAsync(location);

        // then - a HEAD response has no body, so the status and its headers are all the client gets
        head.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        head.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();

        // when
        using var again = await host.HeadAsync(location);

        // then
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        script.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_run_the_handler_once_when_two_status_requests_arrive_together()
    {
        // given - an upload whose handler is owed, and a handler that holds its second run until it is released
        var script = new HandlerScript();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        script.Then(
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);

                return TusUploadHandoff.Keep;
            }
        );
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when - the second request arrives while the first one's handler runs under the upload's lock
        var first = host.HeadAsync(location);
        await entered.Task.WaitAsync(_SignalTimeout, AbortToken);
        using var second = await host.HeadAsync(location);
        release.SetResult();
        using var firstResponse = await first;

        // then
        second.StatusCode.Should().Be(HttpStatusCode.Locked);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResponse.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_answer_423_when_the_store_fails_while_a_status_request_looks_for_an_owed_handler()
    {
        // given
        HandoffDiskStore? store = null;
        var script = new HandlerScript();
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        var setup = _Handled(script, created => store = created, useKestrel: true);
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        store!.FailsHandoffReads = true;
        using var head = await host.HeadAsync(location);

        // then - any other failing status would make a tus client send the whole upload again
        head.StatusCode.Should().Be(HttpStatusCode.Locked);
        script.Uploads.Should().ContainSingle();

        // when
        store.FailsHandoffReads = false;
        using var retried = await host.HeadAsync(location);

        // then
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        script.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_delete_an_incomplete_upload_when_the_client_asks()
    {
        // given
        var script = new HandlerScript();
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content[..4], offset: 0);

        // when
        using var delete = await host.DeleteAsync(location);

        // then
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        script.Uploads.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_a_complete_upload_without_running_its_owed_handler_when_the_client_asks()
    {
        // given - a complete upload whose handler failed, so it was not handed off
        var script = new HandlerScript();
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script, useKestrel: true), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        using var delete = await host.DeleteAsync(location);

        // then - the application is not handed a file its uploader is cancelling
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_403_when_the_client_deletes_an_upload_that_was_handed_off()
    {
        // given
        var script = new HandlerScript();
        await using var host = await TusEndpointTestHost.StartAsync(_Handled(script), AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // when
        using var delete = await host.DeleteAsync(location);

        // then - the upload is the application's now
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
        (await delete.Content.ReadAsStringAsync(AbortToken)).Should().NotBeEmpty();

        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_run_the_handler_once_on_a_store_that_cannot_record_a_handoff()
    {
        // given - tusdotnet's disk store
        var script = new HandlerScript();
        script.Then(static (_, _) => throw new IOException("The queue is unreachable."));
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(disk)
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services => services.AddSingleton(script),
            UseKestrel = true,
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);
        using var head = await host.HeadAsync(location);
        using var delete = await host.DeleteAsync(location);

        // then - nothing tells the status request that the handler is owed
        patch.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().ContainSingle();

        // and nothing tells a delete that it is not, so a complete upload counts as the application's
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_warn_at_startup_for_each_endpoint_whose_handler_cannot_run_again()
    {
        // given - a handler on two endpoints and none on a third, over a store that cannot record a handoff
        using var log = new RecordingLoggerProvider();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
            {
                tus.UseStore(disk);
                tus.AddUploadEndpoint(static options => options.MaxSize = 1000).OnCompleted<ScriptedHandler>();
                tus.AddUploadEndpoint("videos", static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>();
                tus.AddUploadEndpoint("avatars", static options => options.MaxSize = 1000);
            },
            ConfigureServices = services =>
                services.AddSingleton(new HandlerScript()).AddSingleton<ILoggerProvider>(log),
            Map = static endpoints =>
            {
                endpoints.MapHeadlessTus("/files");
                endpoints.MapHeadlessTus("/videos", "videos");
                endpoints.MapHeadlessTus("/avatars", "avatars");
            },
        };

        // when
        await using var _ = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        var warnings = log.Entries.Where(static entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2);
        warnings.Should().ContainSingle(static entry => entry.Message.Contains("The default upload endpoint"));
        warnings.Should().ContainSingle(static entry => entry.Message.Contains("\"videos\""));
        warnings.Should().OnlyContain(static entry => entry.Message.Contains(nameof(TusDiskStore)));
    }

    [Fact]
    public async Task should_not_warn_at_startup_when_the_store_records_a_handoff()
    {
        // given
        using var log = new RecordingLoggerProvider();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(new HandoffDiskStore(disk))
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services =>
                services.AddSingleton(new HandlerScript()).AddSingleton<ILoggerProvider>(log),
        };

        // when
        await using var _ = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        log.Entries.Should().NotContain(static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task should_use_the_handler_the_application_registered()
    {
        // given - the application registers the handler itself, as one instance for the host
        var handler = new CountingHandler();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(new HandoffDiskStore(disk))
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OnCompleted<CountingHandler>(),
            ConfigureServices = services => services.AddSingleton(handler),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // then
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_count_a_handler_that_deleted_the_upload_itself_as_a_handoff()
    {
        // given - a handler that moves the upload elsewhere and deletes it through the store
        HandoffDiskStore? store = null;
        var script = new HandlerScript
        {
            Run = async (upload, cancellationToken) =>
            {
                await store!.DeleteFileAsync(upload.FileId, cancellationToken);

                return TusUploadHandoff.Keep;
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(
            _Handled(script, created => store = created),
            AbortToken
        );
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // then - nothing is left to record as handed off, and that is not a failure
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("11");
        script.Uploads.Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_to_open_the_content_on_a_store_that_cannot_return_an_upload()
    {
        // given - a store with tusdotnet's creation extension and nothing else
        NotSupportedException? failure = null;
        var script = new HandlerScript
        {
            Run = async (upload, cancellationToken) =>
            {
                try
                {
                    await using var content = await upload.OpenContentAsync(cancellationToken);
                }
                catch (NotSupportedException exception)
                {
                    failure = exception;
                }

                return TusUploadHandoff.Keep;
            },
        };
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(new CreationOnlyTusStore(disk))
                    .AddUploadEndpoint(static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services => services.AddSingleton(script),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(_Content.Length);

        // when
        using var patch = await host.PatchAsync(location, _Content, offset: 0);

        // then
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        failure.Should().NotBeNull();
        failure.Message.Should().Contain(nameof(CreationOnlyTusStore));
    }

    [Fact]
    public async Task should_hand_off_an_empty_upload_when_a_deferred_length_is_declared_zero_on_an_endpoint_that_refuses_empty_uploads()
    {
        // given - tusdotnet's disk store, which can defer a length, on an endpoint that refuses a declared length of 0
        var script = new HandlerScript();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, disk) =>
                tus.UseStore(disk)
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = 1000;
                        options.AllowEmptyUploads = false;
                    })
                    .OnCompleted<ScriptedHandler>(),
            ConfigureServices = services => services.AddSingleton(script),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: null);

        // when - the upload's length is declared only once it is known to have no bytes
        using var patch = await host.PatchAsync(location, [], offset: 0, uploadLength: 0);

        // then - a deferred length is not empty at creation, so the rule for empty uploads never applied to it
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        script.Uploads.Should().ContainSingle().Which.Length.Should().Be(0);
    }

    [Fact]
    public void should_refuse_a_second_completion_handler_on_one_endpoint()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessTus(static tus =>
                tus.UseStore(new TusDiskStore(Path.GetTempPath()))
                    .AddUploadEndpoint("videos", static options => options.MaxSize = 1000)
                    .OnCompleted<ScriptedHandler>()
                    .OnCompleted<CountingHandler>()
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "The upload endpoint \"videos\" already has the completion handler ScriptedHandler. An upload "
                    + "endpoint has one completion handler."
            );
    }

    [Fact]
    public void should_refuse_a_rejection_without_a_reason()
    {
        // when
        var blank = () => TusUploadHandoff.Reject(" ");
        var rejected = TusUploadHandoff.Reject("The file holds a virus.");

        // then
        blank.Should().Throw<ArgumentException>();
        rejected.IsRejected.Should().BeTrue();
        rejected.Message.Should().Be("The file holds a virus.");
        TusUploadHandoff.Keep.IsRejected.Should().BeFalse();
        TusUploadHandoff.Delete.IsRejected.Should().BeFalse();
        TusUploadHandoff.Delete.Should().NotBeSameAs(TusUploadHandoff.Keep);
    }

    [Fact]
    public void should_refuse_a_finished_upload_without_a_way_to_open_its_content()
    {
        // when
        var act = () =>
            new TusCompletedUpload(null!)
            {
                FileId = "upload",
                Length = 0,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal),
                TenantId = null,
                OwnerKey = "alice",
            };

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Sends a creating request for an upload that is complete when the request ends: one that carries all of the
    /// upload's bytes, or one for an upload that has none.
    /// </summary>
    private static Task<HttpResponseMessage> _CreateCompleteAsync(TusEndpointTestHost host, bool withData)
    {
        return withData
            ? host.CreateAsync(
                _Content.Length,
                configure: static request =>
                    request.Content = new ByteArrayContent(_Content)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/offset+octet-stream") },
                    }
            )
            : host.CreateAsync(uploadLength: 0);
    }

    private static async Task _WaitAsync(SemaphoreSlim signal)
    {
        (await signal.WaitAsync(_SignalTimeout, AbortToken)).Should().BeTrue("the host raises the signal");
    }

    /// <summary>
    /// Adds middleware that releases <paramref name="ended"/> when the server is done with a
    /// <paramref name="method"/> request, whether it answered it or the client abandoned it.
    /// </summary>
    private static void _SignalWhenTheRequestEnds(IApplicationBuilder app, string method, SemaphoreSlim ended)
    {
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                finally
                {
                    if (HttpMethods.Equals(context.Request.Method, method))
                    {
                        ended.Release();
                    }
                }
            }
        );
    }

    /// <summary>
    /// What the host's <see cref="ScriptedHandler"/> does on each run: the steps queued with <see cref="Then"/>,
    /// in order, and <see cref="Run"/> once they are used up. It records every upload the handler was given.
    /// </summary>
    private sealed class HandlerScript
    {
        private readonly ConcurrentQueue<
            Func<TusCompletedUpload, CancellationToken, ValueTask<TusUploadHandoff>>
        > _steps = new();

        public Func<TusCompletedUpload, CancellationToken, ValueTask<TusUploadHandoff>> Run { get; init; } =
            static (_, _) => ValueTask.FromResult(TusUploadHandoff.Keep);

        /// <summary>The upload of every run, in order.</summary>
        public ConcurrentQueue<TusCompletedUpload> Uploads { get; } = new();

        /// <summary>The signed-in user each handler instance was built for, in order.</summary>
        public ConcurrentQueue<string?> ResolvedAs { get; } = new();

        public void Then(Func<TusCompletedUpload, CancellationToken, ValueTask<TusUploadHandoff>> step)
        {
            _steps.Enqueue(step);
        }

        public ValueTask<TusUploadHandoff> RunAsync(TusCompletedUpload upload, CancellationToken cancellationToken)
        {
            Uploads.Enqueue(upload);

            return _steps.TryDequeue(out var step) ? step(upload, cancellationToken) : Run(upload, cancellationToken);
        }
    }

    private sealed class ScriptedHandler : ITusUploadCompletedHandler
    {
        private readonly HandlerScript _script;

        public ScriptedHandler(HandlerScript script, ICurrentUser currentUser)
        {
            _script = script;
            script.ResolvedAs.Enqueue(currentUser.UserId?.GetUnderlyingPrimitiveValue());
        }

        public ValueTask<TusUploadHandoff> HandleAsync(TusCompletedUpload upload, CancellationToken cancellationToken)
        {
            return _script.RunAsync(upload, cancellationToken);
        }
    }

    private sealed class CountingHandler : ITusUploadCompletedHandler
    {
        private int _runs;

        public int Runs => _runs;

        public ValueTask<TusUploadHandoff> HandleAsync(TusCompletedUpload upload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);

            return ValueTask.FromResult(TusUploadHandoff.Keep);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    /// <summary>Keeps what the host logs.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(Entries);
        }

        public void Dispose() { }

        private sealed class RecordingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
            }
        }
    }
}
