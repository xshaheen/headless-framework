// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Mediator;
using Headless.Mediator.Behaviors;
using Mediator;

namespace Tests;

public sealed class TenantRequiredBehaviorTests
{
    [Fact]
    public async Task should_invoke_next_when_tenant_is_available()
    {
        // given
        var response = new TestResponse();
        var currentTenant = _CreateCurrentTenant("acme");
        var behavior = new TenantRequiredBehavior<TestRequest, TestResponse>(currentTenant);
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new TestRequest(),
            _CreateNext<TestRequest>(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_next_when_allow_missing_tenant_attribute_is_present_and_tenant_is_available()
    {
        // given
        var response = new TestResponse();
        var currentTenant = _CreateCurrentTenant("acme");
        var behavior = new TenantRequiredBehavior<AllowMissingTestRequest, TestResponse>(currentTenant);
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new AllowMissingTestRequest(),
            _CreateNext<AllowMissingTestRequest>(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_next_when_allow_missing_tenant_attribute_is_present_and_tenant_is_missing()
    {
        // given
        var response = new TestResponse();
        var currentTenant = _CreateCurrentTenant(null);
        var behavior = new TenantRequiredBehavior<AllowMissingTestRequest, TestResponse>(currentTenant);
        var callCount = 0;

        // when
        var result = await behavior.Handle(
            new AllowMissingTestRequest(),
            _CreateNext<AllowMissingTestRequest>(response, () => callCount++),
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(response);
        callCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_missing_tenant_context_exception_when_tenant_is_missing(string? tenantId)
    {
        // given
        var response = new TestResponse();
        var currentTenant = _CreateCurrentTenant(tenantId);
        var behavior = new TenantRequiredBehavior<TestRequest, TestResponse>(currentTenant);
        var callCount = 0;

        // when
        var action = async () =>
            await behavior.Handle(
                new TestRequest(),
                _CreateNext<TestRequest>(response, () => callCount++),
                CancellationToken.None
            );

        // then
        var exception = await action.Should().ThrowExactlyAsync<MissingTenantContextException>();
        exception.Which.Data.Count.Should().Be(0);
        callCount.Should().Be(0);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_current_tenant_is_null()
    {
        // given
        ICurrentTenant? currentTenant = null;

        // when
        var action = () => new TenantRequiredBehavior<TestRequest, TestResponse>(currentTenant!);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(currentTenant));
    }

    [Fact]
    public void should_cache_allow_missing_tenant_attribute_per_closed_generic_type()
    {
        // given
        var field = typeof(TenantRequiredBehavior<AllowMissingTestRequest, TestResponse>).GetField(
            "_AllowMissingTenant",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
        );

        // when
        var value = field?.GetValue(obj: null);

        // then
        field.Should().NotBeNull();
        field!.IsInitOnly.Should().BeTrue();
        field.IsStatic.Should().BeTrue();
        value.Should().Be(true);
    }

    private static ICurrentTenant _CreateCurrentTenant(string? tenantId)
    {
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(tenantId);

        return currentTenant;
    }

    private static MessageHandlerDelegate<TRequest, TestResponse> _CreateNext<TRequest>(
        TestResponse response,
        Action onInvoke
    )
        where TRequest : IRequest<TestResponse>
    {
        return (_, _) =>
        {
            onInvoke();

            return new ValueTask<TestResponse>(response);
        };
    }

    private sealed record TestRequest : IRequest<TestResponse>;

    [AllowMissingTenant]
    private sealed record AllowMissingTestRequest : IRequest<TestResponse>;

    private sealed record TestResponse;
}
