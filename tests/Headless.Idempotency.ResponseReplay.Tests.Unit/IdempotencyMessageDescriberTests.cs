// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Idempotency.Resources;

namespace Tests;

public sealed class IdempotencyMessageDescriberTests
{
    [Fact]
    public void error_code_constants_match_descriptor_codes()
    {
        IdempotencyMessageDescriber.KeyReused().Code.Should().Be(HttpIdempotencyErrorCodes.KeyReused);
        IdempotencyMessageDescriber.InFlight().Code.Should().Be(HttpIdempotencyErrorCodes.InFlight);
        IdempotencyMessageDescriber.InFlightTimeout().Code.Should().Be(HttpIdempotencyErrorCodes.InFlightTimeout);
        IdempotencyMessageDescriber.BodyTooLarge().Code.Should().Be(HttpIdempotencyErrorCodes.BodyTooLarge);
        IdempotencyMessageDescriber.KeyMalformed().Code.Should().Be(HttpIdempotencyErrorCodes.KeyMalformed);
        IdempotencyMessageDescriber.KeyRequired().Code.Should().Be(HttpIdempotencyErrorCodes.KeyRequired);
    }

    [Fact]
    public void should_return_expected_code_when_key_reused()
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        descriptor.Code.Should().Be("g:idempotency_key_reused");
    }

    [Fact]
    public void should_return_non_empty_description_when_key_reused()
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void should_return_expected_code_when_in_flight()
    {
        var descriptor = IdempotencyMessageDescriber.InFlight();

        descriptor.Code.Should().Be("g:idempotency_in_flight");
    }

    [Fact]
    public void should_return_non_empty_description_when_in_flight()
    {
        var descriptor = IdempotencyMessageDescriber.InFlight();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void should_return_expected_code_when_in_flight_timeout()
    {
        var descriptor = IdempotencyMessageDescriber.InFlightTimeout();

        descriptor.Code.Should().Be("g:idempotency_in_flight_timeout");
    }

    [Fact]
    public void should_return_non_empty_description_when_in_flight_timeout()
    {
        var descriptor = IdempotencyMessageDescriber.InFlightTimeout();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void should_return_expected_code_when_body_too_large()
    {
        var descriptor = IdempotencyMessageDescriber.BodyTooLarge();

        descriptor.Code.Should().Be("g:idempotency_body_too_large");
    }

    [Fact]
    public void should_return_non_empty_description_when_body_too_large()
    {
        var descriptor = IdempotencyMessageDescriber.BodyTooLarge();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void should_return_expected_code_when_key_malformed()
    {
        var descriptor = IdempotencyMessageDescriber.KeyMalformed();

        descriptor.Code.Should().Be("g:idempotency_key_malformed");
    }

    [Fact]
    public void should_return_expected_code_when_key_required()
    {
        var descriptor = IdempotencyMessageDescriber.KeyRequired();

        descriptor.Code.Should().Be("g:idempotency_key_required");
    }

    [Fact]
    public void should_return_non_empty_description_when_key_required()
    {
        var descriptor = IdempotencyMessageDescriber.KeyRequired();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }
}
