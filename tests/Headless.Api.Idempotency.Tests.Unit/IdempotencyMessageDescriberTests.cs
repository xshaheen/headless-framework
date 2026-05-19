// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;

namespace Tests;

public sealed class IdempotencyMessageDescriberTests
{
    [Fact]
    public void key_reused_should_return_expected_code()
    {
        var descriptor = IdempotencyMessageDescriber.KeyReused();

        descriptor.Code.Should().Be("g:idempotency-key-reused");
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

        descriptor.Code.Should().Be("g:idempotency-in-flight");
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

        descriptor.Code.Should().Be("g:idempotency-in-flight-timeout");
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

        descriptor.Code.Should().Be("g:idempotency-body-too-large");
    }

    [Fact]
    public void body_too_large_should_return_non_empty_description()
    {
        var descriptor = IdempotencyMessageDescriber.BodyTooLarge();

        descriptor.Description.Should().NotBeNullOrEmpty();
    }
}
