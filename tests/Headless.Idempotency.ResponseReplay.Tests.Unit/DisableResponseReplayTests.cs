// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class DisableResponseReplayTests : IdempotencyMiddlewareTestBase
{
    [Fact]
    public async Task should_run_the_handler_without_admitting_the_key_when_the_endpoint_opted_out()
    {
        // given
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = _WithEndpoint(CreateContext(idempotencyKey: "k1"), new DisableResponseReplayAttribute());
        var calls = 0;

        // when
        await middleware.InvokeAsync(context, _ => _Run(() => calls++));

        // then
        calls.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.GetHttpIdempotencyContext().Should().BeNull();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_ignore_a_malformed_key_when_the_endpoint_opted_out()
    {
        // given
        var middleware = CreateMiddleware();
        var context = _WithEndpoint(
            CreateContext(idempotencyKey: new string('k', 300)),
            new DisableResponseReplayAttribute()
        );
        var calls = 0;

        // when
        await middleware.InvokeAsync(context, _ => _Run(() => calls++));

        // then
        calls.Should().Be(1, "the key is not validated on an opted-out endpoint");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_not_refuse_a_keyless_request_when_the_endpoint_opted_out_of_a_required_key()
    {
        // given
        var middleware = CreateMiddleware(options: Monitor(new HttpIdempotencyOptions { KeyRequired = true }));
        var context = _WithEndpoint(
            CreateContext(),
            new HttpIdempotencyMetadata(static o => o.KeyRequired = true),
            new DisableResponseReplayAttribute()
        );
        var calls = 0;

        // when
        await middleware.InvokeAsync(context, _ => _Run(() => calls++));

        // then
        calls.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_still_admit_the_key_when_the_endpoint_did_not_opt_out()
    {
        // given
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = _WithEndpoint(CreateContext(idempotencyKey: "k1"));

        // when
        await middleware.InvokeAsync(context, static _ => Task.CompletedTask);

        // then
        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_add_the_opt_out_metadata_when_disable_response_replay_is_called()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Post);

        // when
        _ApplyConventions(endpoint, static builder => builder.DisableResponseReplay());

        // then
        endpoint.Metadata.OfType<DisableResponseReplayAttribute>().Should().ContainSingle();
        endpoint.Metadata.OfType<HttpContractMetadata>().Should().BeEmpty();
    }

    [Fact]
    public void should_attach_no_contract_when_the_endpoint_opts_out_after_a_required_key()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Post);

        // when: the order a group's RequireIdempotencyKey() and the endpoint's DisableResponseReplay() run in
        _ApplyConventions(
            endpoint,
            static builder => builder.RequireIdempotencyKey().WithIdempotency(_ => { }).DisableResponseReplay()
        );

        // then
        endpoint.Metadata.OfType<HttpContractMetadata>().Should().BeEmpty();
    }

    [Fact]
    public void should_attach_no_contract_when_the_endpoint_requires_a_key_after_opting_out()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Post);
        var other = new HttpContractMetadata();
        endpoint.Metadata.Add(other);

        // when: the order a group's DisableResponseReplay() and the endpoint's RequireIdempotencyKey() run in
        _ApplyConventions(endpoint, static builder => builder.DisableResponseReplay().RequireIdempotencyKey());

        // then
        endpoint.Metadata.OfType<HttpContractMetadata>().Should().ContainSingle().Which.Should().BeSameAs(other);
    }

    private static DefaultHttpContext _WithEndpoint(DefaultHttpContext context, params object[] metadata)
    {
        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(metadata),
            displayName: "test"
        );
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        return context;
    }

    private static Task _Run(Action action)
    {
        action();

        return Task.CompletedTask;
    }

    private static RouteEndpointBuilder _CreateEndpoint(string method)
    {
        var services = new ServiceCollection();
        services.AddOptions<HttpIdempotencyOptions>();

        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0)
        {
            ApplicationServices = services.BuildServiceProvider(),
        };
        endpoint.Metadata.Add(new HttpMethodMetadata([method]));

        return endpoint;
    }

    // Runs the conventions in registration order against one endpoint, as the routing data source does.
    private static void _ApplyConventions(EndpointBuilder endpoint, Action<ConventionRecorder> declare)
    {
        var recorder = new ConventionRecorder();
        declare(recorder);

        foreach (var convention in recorder.Conventions)
        {
            convention(endpoint);
        }
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }

    private sealed class ConventionRecorder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }
}
