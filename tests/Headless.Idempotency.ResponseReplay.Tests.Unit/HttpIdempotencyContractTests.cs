// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HttpIdempotencyContractTests : TestBase
{
    [Fact]
    public void should_describe_an_optional_key_and_every_default_rejection()
    {
        // when
        var contract = HttpIdempotencyContract.Create("Idempotency-Key", new HttpIdempotencyOptions());

        // then
        var header = contract.RequestHeaders.Should().ContainSingle().Subject;
        header.Name.Should().Be("Idempotency-Key");
        header.Required.Should().BeFalse();

        contract.Responses.Select(x => x.StatusCode).Should().Equal(400, 409, 422, 413);
        _Description(contract, 400).Should().Contain(HttpIdempotencyErrorCodes.KeyMalformed);
        _Description(contract, 400).Should().NotContain(HttpIdempotencyErrorCodes.KeyRequired);
        _Description(contract, 409).Should().Contain(HttpIdempotencyErrorCodes.InFlight);
        _Description(contract, 409).Should().NotContain(HttpIdempotencyErrorCodes.KeyReused);
        _Description(contract, 422).Should().Contain(HttpIdempotencyErrorCodes.KeyReused);
        _Description(contract, 413).Should().Contain(HttpIdempotencyErrorCodes.BodyTooLarge);

        contract
            .ResponseHeaders.Select(x => (x.Name, x.MinStatusCode, x.MaxStatusCode))
            .Should()
            .Equal(("Retry-After", 409, 409), ("Idempotent-Replayed", 200, 299));
    }

    [Fact]
    public void should_follow_the_key_requirement_mismatch_status_wait_and_oversize_options()
    {
        // given
        var options = new HttpIdempotencyOptions
        {
            KeyRequired = true,
            MismatchStatusCode = StatusCodes.Status409Conflict,
            InFlightStrategy = InFlightStrategy.WaitAndReplay,
            OversizeBehavior = OversizeBehavior.PassThrough,
        };

        // when
        var contract = HttpIdempotencyContract.Create("X-Key", options);

        // then
        contract.RequestHeaders.Should().ContainSingle().Which.Required.Should().BeTrue();
        contract.Responses.Select(x => x.StatusCode).Should().Equal(400, 409);
        _Description(contract, 400).Should().Contain(HttpIdempotencyErrorCodes.KeyRequired).And.Contain("X-Key");
        _Description(contract, 409)
            .Should()
            .Contain(HttpIdempotencyErrorCodes.InFlightTimeout)
            .And.Contain(HttpIdempotencyErrorCodes.KeyReused);
    }

    [Fact]
    public void should_attach_one_contract_reflecting_every_override_and_the_application_header_name()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Post, options => options.HeaderName = "X-Request-Key");

        // when
        _ApplyConventions(
            endpoint,
            builder =>
                builder
                    .WithIdempotency(options =>
                    {
                        options.HeaderName = "Ignored";
                        options.MismatchStatusCode = StatusCodes.Status409Conflict;
                    })
                    .RequireIdempotencyKey()
        );

        // then
        var contract = endpoint.Metadata.OfType<HttpContractMetadata>().Should().ContainSingle().Subject;
        var header = contract.RequestHeaders.Should().ContainSingle().Subject;
        header.Name.Should().Be("X-Request-Key");
        header.Required.Should().BeTrue();
        contract.Responses.Select(x => x.StatusCode).Should().NotContain(422);
    }

    [Fact]
    public void should_keep_a_contract_another_feature_attached()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Post);
        var other = new HttpContractMetadata();
        endpoint.Metadata.Add(other);

        // when
        _ApplyConventions(endpoint, builder => builder.RequireIdempotencyKey().WithIdempotency(_ => { }));

        // then
        endpoint.Metadata.OfType<HttpContractMetadata>().Should().HaveCount(2).And.Contain(other);
    }

    [Fact]
    public void should_attach_no_contract_when_no_endpoint_method_is_an_idempotency_method()
    {
        // given
        var endpoint = _CreateEndpoint(HttpMethods.Get);

        // when
        _ApplyConventions(endpoint, builder => builder.RequireIdempotencyKey());

        // then
        endpoint.Metadata.OfType<HttpContractMetadata>().Should().BeEmpty();
    }

    private static string _Description(HttpContractMetadata contract, int statusCode) =>
        contract.Responses.Single(x => x.StatusCode == statusCode).Description;

    private static RouteEndpointBuilder _CreateEndpoint(string method, Action<HttpIdempotencyOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<HttpIdempotencyOptions>().Configure(configure ?? (_ => { }));

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

    private sealed class ConventionRecorder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }
}
