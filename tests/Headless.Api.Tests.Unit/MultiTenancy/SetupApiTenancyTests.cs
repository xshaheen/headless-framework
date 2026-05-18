// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.MultiTenancy;

public sealed class SetupApiTenancyTests
{
    [Fact]
    public void should_register_tenant_authorization_from_headless_tenancy_root()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Authorization(auth => auth.RequireTenant()));

        // then
        builder
            .Services.Where(_IsTenantRequirementHandlerDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        var resultHandlerDescriptor = builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler))
            .Should()
            .ContainSingle()
            .Subject;
        resultHandlerDescriptor.Lifetime.Should().Be(ServiceLifetime.Transient);
        resultHandlerDescriptor.ImplementationFactory.Should().NotBeNull();

        var manifest = _GetManifest(builder.Services);
        var seam = manifest.GetSeam(HeadlessAuthorizationTenancyBuilder.Seam);
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().BeEquivalentTo(HeadlessAuthorizationTenancyBuilder.RequireTenantCapability);
    }

    [Fact]
    public void should_register_tenant_authorization_idempotently()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Authorization(auth =>
            {
                auth.RequireTenant();
                auth.RequireTenant();
            })
        );

        // then
        builder.Services.Where(_IsTenantRequirementHandlerDescriptor).Should().ContainSingle();
        builder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHeadlessTenancyValidator)
                && descriptor.ImplementationType == typeof(HeadlessAuthorizationTenancyValidator)
            )
            .Should()
            .ContainSingle();
        builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task should_delegate_non_tenant_authorization_results_to_existing_customer_handler()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var customerHandler = new RecordingAuthorizationMiddlewareResultHandler();
        builder.Services.AddSingleton(Substitute.For<IProblemDetailsCreator>());
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler>(customerHandler);

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Authorization(auth => auth.RequireTenant()));
        using var serviceProvider = builder.Services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        var failure = AuthorizationFailure.Failed([new DenyAnonymousAuthorizationRequirement()]);
        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Forbid(failure)
        );

        // then
        handler.Should().BeOfType<TenantAuthorizationMiddlewareResultHandler>();
        customerHandler.Calls.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
    }

    [Fact]
    public void should_compose_authorization_with_other_tenancy_seams()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
        );

        // then
        var manifest = _GetManifest(builder.Services);
        manifest.GetSeam(HeadlessHttpTenancyBuilder.Seam).Should().NotBeNull();
        manifest.GetSeam(HeadlessAuthorizationTenancyBuilder.Seam).Should().NotBeNull();
    }

    [Fact]
    public void should_emit_diagnostic_when_authorization_tenant_policy_is_missing()
    {
        // given
        var services = new ServiceCollection();
        services.AddAuthorization();
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(HeadlessAuthorizationTenancyBuilder.Seam, TenantPostureStatus.Enforcing);
        using var serviceProvider = services.BuildServiceProvider();
        var validator = new HeadlessAuthorizationTenancyValidator();

        // when
        var diagnostics = validator.Validate(new HeadlessTenancyValidationContext(serviceProvider, manifest));

        // then
        diagnostics
            .Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(HeadlessAuthorizationTenancyBuilder.AuthorizationPolicyMissingDiagnosticCode);
    }

    [Fact]
    public void should_not_emit_diagnostic_when_default_policy_contains_tenant_requirement()
    {
        // given
        var services = new ServiceCollection();
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRequirement())
                .Build();
        });
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(HeadlessAuthorizationTenancyBuilder.Seam, TenantPostureStatus.Enforcing);
        using var serviceProvider = services.BuildServiceProvider();
        var validator = new HeadlessAuthorizationTenancyValidator();

        // when
        var diagnostics = validator.Validate(new HeadlessTenancyValidationContext(serviceProvider, manifest));

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void should_not_emit_diagnostic_when_fallback_policy_contains_tenant_requirement()
    {
        // given
        var services = new ServiceCollection();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRequirement())
                .Build();
        });
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(HeadlessAuthorizationTenancyBuilder.Seam, TenantPostureStatus.Enforcing);
        using var serviceProvider = services.BuildServiceProvider();
        var validator = new HeadlessAuthorizationTenancyValidator();

        // when
        var diagnostics = validator.Validate(new HeadlessTenancyValidationContext(serviceProvider, manifest));

        // then
        diagnostics.Should().BeEmpty();
    }

    private static bool _IsTenantRequirementHandlerDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IAuthorizationHandler)
            && descriptor.ImplementationType == typeof(TenantRequirementHandler);
    }

    private static TenantPostureManifest _GetManifest(IServiceCollection services)
    {
        return services
            .Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest))
            .Should()
            .ContainSingle()
            .Subject.ImplementationInstance.Should()
            .BeOfType<TenantPostureManifest>()
            .Subject;
    }

    private sealed class RecordingAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        public int Calls { get; private set; }

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult
        )
        {
            Calls++;
            context.Response.StatusCode = StatusCodes.Status418ImATeapot;

            return Task.CompletedTask;
        }
    }
}
