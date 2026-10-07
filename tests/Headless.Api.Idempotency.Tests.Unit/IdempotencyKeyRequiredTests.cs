// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class IdempotencyKeyRequiredTests : IdempotencyMiddlewareTestBase
{
    [Fact]
    public async Task should_refuse_a_request_without_a_key_with_400_when_the_endpoint_requires_one()
    {
        // given
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var context = _WithEndpoint(CreateContext(), new IdempotencyMetadata(static o => o.KeyRequired = true));
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        // then
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await ReadResponseAsync(context)).Should().Contain(IdempotencyErrorCodes.KeyRequired);
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_refuse_a_blank_key_when_the_endpoint_requires_one(string key)
    {
        // given
        var middleware = CreateMiddleware(options: Monitor(new IdempotencyOptions { KeyRequired = true }));
        var context = CreateContext(idempotencyKey: key);

        // when
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task should_pass_through_a_keyless_request_the_middleware_would_not_handle_even_when_keys_are_required()
    {
        // given
        var middleware = CreateMiddleware(
            options: Monitor(new IdempotencyOptions { KeyRequired = true, ShouldApply = static _ => false })
        );
        var getContext = CreateContext(method: "GET");
        var declinedContext = CreateContext();
        var calls = 0;

        // when
        await middleware.InvokeAsync(getContext, _ => _Run(() => calls++));
        await middleware.InvokeAsync(declinedContext, _ => _Run(() => calls++));

        // then
        calls.Should().Be(2, "a GET is outside Methods and ShouldApply declined the POST");
    }

    [Fact]
    public async Task should_pass_through_a_keyless_request_by_default()
    {
        // given
        var middleware = CreateMiddleware();
        var context = CreateContext();
        var nextCalled = false;

        // when
        await middleware.InvokeAsync(context, _ => _Run(() => nextCalled = true));

        // then
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task should_apply_every_attached_metadata_in_order_so_require_key_and_with_idempotency_compose()
    {
        // given
        var operations = CreateAdmittingOperations();
        var middleware = CreateMiddleware(operations: operations);
        var keyless = _WithEndpoint(
            CreateContext(),
            new IdempotencyMetadata(static o => o.Retention = TimeSpan.FromDays(7)),
            new IdempotencyMetadata(static o => o.KeyRequired = true)
        );
        var keyed = _WithEndpoint(
            CreateContext(idempotencyKey: "k1"),
            new IdempotencyMetadata(static o => o.KeyRequired = true),
            new IdempotencyMetadata(static o => o.Retention = TimeSpan.FromDays(7))
        );

        // when
        await middleware.InvokeAsync(keyless, _ => Task.CompletedTask);
        await middleware.InvokeAsync(
            keyed,
            static ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status201Created;
                return Task.CompletedTask;
            }
        );

        // then
        keyless.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest, "the later metadata still applies");
        await operations
            .Received(1)
            .AdmitAsync(
                Arg.Any<string>(),
                Arg.Any<IdempotencyFingerprint>(),
                Arg.Any<string?>(),
                Arg.Any<TimeSpan?>(),
                TimeSpan.FromDays(7),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_tell_a_client_refused_as_in_flight_when_the_holder_lease_runs_out()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var operations = Substitute.For<IIdempotentOperations>();
        AdmitReturns(
            operations,
            key =>
                IdempotentAdmission.InFlight(
                    new IdempotencyKey(TestTenant, key),
                    IdempotencyFingerprint.Compute("any"),
                    5,
                    clock.GetUtcNow().AddSeconds(41.2)
                )
        );
        var middleware = CreateMiddleware(operations: operations, timeProvider: clock);
        var context = CreateContext(idempotencyKey: "k1");

        // when
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Headers.RetryAfter.ToString().Should().Be("42", "the delay is rounded up to whole seconds");
    }

    [Fact]
    public void should_attach_key_required_metadata_through_the_endpoint_extension()
    {
        // given
        var builder = new RouteGroupBuilderProbe();

        // when
        builder.RequireIdempotencyKey();

        // then
        var probe = new IdempotencyOptions();
        builder.Metadata.Should().ContainSingle().Which.Should().BeOfType<IdempotencyMetadata>().Which.Configure(probe);
        probe.KeyRequired.Should().BeTrue();
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

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }

    private sealed class RouteGroupBuilderProbe : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public List<object> Metadata { get; } = [];

        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention)
        {
            var endpointBuilder = new RouteEndpointBuilder(
                _ => Task.CompletedTask,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/"),
                0
            );
            convention(endpointBuilder);
            Metadata.AddRange(endpointBuilder.Metadata);
        }
    }
}
