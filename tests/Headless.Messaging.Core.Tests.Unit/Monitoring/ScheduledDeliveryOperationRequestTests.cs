// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Monitoring;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ScheduledDeliveryOperationRequestTests : TestBase
{
    private static OperatorAuthorizationContext _CreateValidAuth(string actor = "test-operator") =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, actor)], "test")));

    [Fact]
    public void should_pass_validation_for_valid_request()
    {
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "valid reason",
            _CreateValidAuth()
        );

        var act = () => request.Validate();
        act.Should().NotThrow();
        request.Actor.Should().Be("test-operator");
    }

    [Fact]
    public void should_throw_unauthorized_access_when_authorization_is_null()
    {
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "valid reason",
            null!
        );

        var act = () => request.Validate();
        act.Should().Throw<UnauthorizedAccessException>().WithMessage("*authorization context*");
    }

    [Fact]
    public void should_throw_unauthorized_access_when_principal_is_unauthenticated()
    {
        var unauth = new OperatorAuthorizationContext(new ClaimsPrincipal(new ClaimsIdentity()));
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "valid reason",
            unauth
        );

        var act = () => request.Validate();
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void should_throw_invalid_operation_when_operation_id_is_empty()
    {
        var request = new ScheduledDeliveryOperationRequest(
            Guid.Empty,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "valid reason",
            _CreateValidAuth()
        );

        var act = () => request.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*operation identity*");
    }

    [Fact]
    public void should_throw_invalid_operation_when_storage_id_is_empty()
    {
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow,
            "valid reason",
            _CreateValidAuth()
        );

        var act = () => request.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Storage identity*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_invalid_operation_when_reason_is_empty(string? reason)
    {
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            reason!,
            _CreateValidAuth()
        );

        var act = () => request.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*reason*");
    }

    [Fact]
    public void should_throw_invalid_operation_when_reason_exceeds_max_length()
    {
        var longReason = new string('a', ScheduledDeliveryOperationRequest.ReasonMaxLength + 1);
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            longReason,
            _CreateValidAuth()
        );

        var act = () => request.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*reason*");
    }

    [Fact]
    public void should_validate_query_storage_ids_count_boundary()
    {
        var validQuery = new ScheduledDeliveryQuery
        {
            StorageIds = Enumerable
                .Range(0, ScheduledDeliveryQuery.MaxStorageIds)
                .Select(_ => Guid.NewGuid())
                .ToArray(),
        };
        var validAct = () => validQuery.Validate();
        validAct.Should().NotThrow();

        var invalidQuery = new ScheduledDeliveryQuery
        {
            StorageIds = Enumerable
                .Range(0, ScheduledDeliveryQuery.MaxStorageIds + 1)
                .Select(_ => Guid.NewGuid())
                .ToArray(),
        };
        var invalidAct = () => invalidQuery.Validate();
        invalidAct.Should().Throw<InvalidOperationException>().WithMessage("*Storage IDs*");
    }
}
