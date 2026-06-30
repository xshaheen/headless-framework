// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
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

        // RequireTenant() must not register an IAuthorizationMiddlewareResultHandler — the
        // structured g:tenant_required 403 is produced by StatusCodesRewriterMiddleware reading
        // a HttpContext.Items marker, not by decorating the result handler. Verifying the absence
        // guards against regressing back into the ordering-sensitive design.
        builder
            .Services.Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));

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
    }

    [Fact]
    public void should_preserve_consumer_authorization_result_handler_registered_after_require_tenant()
    {
        // given - regression guard: the old decorator design forced consumers to register their
        // custom IAuthorizationMiddlewareResultHandler BEFORE RequireTenant(); reversing the order
        // silently disabled the framework's tenant 403 mapping. The new marker-based design must
        // not register anything against IAuthorizationMiddlewareResultHandler, so a consumer
        // registration in any order continues to be the resolved handler unchanged.
        var builder = Host.CreateApplicationBuilder();
        var customerHandler = new RecordingAuthorizationMiddlewareResultHandler();

        // when - register the consumer handler AFTER RequireTenant().
        builder.AddHeadlessTenancy(tenancy => tenancy.Authorization(auth => auth.RequireTenant()));
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler>(customerHandler);

        // then
        using var serviceProvider = builder.Services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        handler.Should().BeSameAs(customerHandler);
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

        services
            .AddAuthorizationBuilder()
            .SetDefaultPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TenantRequirement())
                    .Build()
            );

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

        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TenantRequirement())
                    .Build()
            );

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
    public void should_emit_diagnostic_when_authorization_options_are_unregistered()
    {
        // given - ServiceCollection without AddAuthorization(); IOptions<AuthorizationOptions> is unresolvable.
        var services = new ServiceCollection();
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(HeadlessAuthorizationTenancyBuilder.Seam, TenantPostureStatus.Enforcing);
        using var serviceProvider = services.BuildServiceProvider();
        var validator = new HeadlessAuthorizationTenancyValidator();

        // when
        var diagnostics = validator.Validate(new HeadlessTenancyValidationContext(serviceProvider, manifest)).ToList();

        // then - same diagnostic as the "no TenantRequirement on either policy" branch.
        diagnostics
            .Should()
            .Contain(diagnostic =>
                diagnostic.Code == HeadlessAuthorizationTenancyBuilder.AuthorizationPolicyMissingDiagnosticCode
            );
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
        public Task HandleAsync(
            Microsoft.AspNetCore.Http.RequestDelegate next,
            Microsoft.AspNetCore.Http.HttpContext context,
            AuthorizationPolicy policy,
            Microsoft.AspNetCore.Authorization.Policy.PolicyAuthorizationResult authorizeResult
        )
        {
            return Task.CompletedTask;
        }
    }
}
