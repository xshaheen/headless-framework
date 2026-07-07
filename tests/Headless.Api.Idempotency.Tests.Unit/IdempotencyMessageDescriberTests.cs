// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Api.Idempotency.Resources;

namespace Tests;

public sealed class IdempotencyMessageDescriberTests
{
    [Fact]
    public void error_code_constants_match_descriptor_codes()
    {
        IdempotencyMessageDescriber.KeyReused().Code.Should().Be(IdempotencyErrorCodes.KeyReused);
        IdempotencyMessageDescriber.InFlight().Code.Should().Be(IdempotencyErrorCodes.InFlight);
        IdempotencyMessageDescriber.InFlightTimeout().Code.Should().Be(IdempotencyErrorCodes.InFlightTimeout);
        IdempotencyMessageDescriber.BodyTooLarge().Code.Should().Be(IdempotencyErrorCodes.BodyTooLarge);
        IdempotencyMessageDescriber.KeyMalformed().Code.Should().Be(IdempotencyErrorCodes.KeyMalformed);
    }

    [Fact]
    public void key_reused_should_return_expected_code()
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        descriptor.Code.Should().Be("g:idempotency_key_reused");
    }

    [Fact]
    public void key_reused_should_return_non_empty_description()
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void in_flight_should_return_expected_code()
    {
        var descriptor = IdempotencyMessageDescriber.InFlight();

        descriptor.Code.Should().Be("g:idempotency_in_flight");
    }

    [Fact]
    public void in_flight_should_return_non_empty_description()
    {
        var descriptor = IdempotencyMessageDescriber.InFlight();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void in_flight_timeout_should_return_expected_code()
    {
        var descriptor = IdempotencyMessageDescriber.InFlightTimeout();

        descriptor.Code.Should().Be("g:idempotency_in_flight_timeout");
    }

    [Fact]
    public void in_flight_timeout_should_return_non_empty_description()
    {
        var descriptor = IdempotencyMessageDescriber.InFlightTimeout();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void body_too_large_should_return_expected_code()
    {
        var descriptor = IdempotencyMessageDescriber.BodyTooLarge();

        descriptor.Code.Should().Be("g:idempotency_body_too_large");
    }

    [Fact]
    public void body_too_large_should_return_non_empty_description()
    {
        var descriptor = IdempotencyMessageDescriber.BodyTooLarge();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }
}
