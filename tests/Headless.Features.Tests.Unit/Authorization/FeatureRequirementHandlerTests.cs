// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Security.Claims;
using Headless;
using Headless.Features;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Authorization;

public sealed class FeatureRequirementHandlerTests : TestBase
{
    private const string _Reports = "Reports";
    private const string _Exports = "Exports";

    [Fact]
    public async Task should_succeed_when_the_required_feature_is_enabled()
    {
        // given
        await using var provider = _CreateProvider(_Reports);
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new FeatureRequirement([_Reports], false))
            .Build();

        // when
        var result = await _AuthorizeAsync(provider, policy);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_with_the_feature_unavailable_reason_when_the_feature_is_disabled()
    {
        // given
        await using var provider = _CreateProvider();
        var requirement = new FeatureRequirement([_Reports], requiresAll: false);
        var policy = new AuthorizationPolicyBuilder().AddRequirements(requirement).Build();

        // when
        var result = await _AuthorizeAsync(provider, policy);

        // then - the reason is caller-facing text, never the feature name
        result.Succeeded.Should().BeFalse();
        result.Failure!.FailCalled.Should().BeTrue();
        result
            .Failure.FailureReasons.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Be("This feature is currently unavailable.")
            .And.NotContain(_Reports);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task should_honor_requires_all(bool requiresAll, bool expected)
    {
        // given - one of the two features is on
        await using var provider = _CreateProvider(_Reports);
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new FeatureRequirement([_Reports, _Exports], requiresAll))
            .Build();

        // when
        var result = await _AuthorizeAsync(provider, policy);

        // then
        result.Succeeded.Should().Be(expected);
    }

    [Fact]
    public async Task should_satisfy_every_feature_requirement_when_the_policy_disables_the_feature_check()
    {
        // given - the marker comes after the requirements it switches off, as a method-level opt-out does
        await using var provider = _CreateProvider();
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements([.. new RequiresFeatureAttribute(_Reports).GetRequirements()])
            .AddRequirements([.. new DisableFeatureCheckAttribute().GetRequirements()])
            .Build();

        // when
        var result = await _AuthorizeAsync(provider, policy);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_declare_no_requirement_for_an_attribute_without_features()
    {
        // when
        var requirements = new RequiresFeatureAttribute().GetRequirements();

        // then - matches EnsureEnabledAsync, which treats an empty list as satisfied
        requirements.Should().BeEmpty();
    }

    [Fact]
    public void should_declare_the_attribute_features_and_mode_as_the_requirement()
    {
        // when
        var requirement = new RequiresFeatureAttribute(_Reports, _Exports) { IsAnd = true }
            .GetRequirements()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<FeatureRequirement>()
            .Subject;

        // then
        requirement.FeatureNames.Should().Equal(_Reports, _Exports);
        requirement.RequiresAll.Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_requirement_without_features()
    {
        // when
        var act = () => new FeatureRequirement([], requiresAll: false);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_keep_checking_the_attribute_outside_http()
    {
        // given
        await using var provider = _CreateProvider();
        var checker = provider.GetRequiredService<IMethodInvocationFeatureCheckerService>();
        var method = typeof(GatedService).GetMethod(
            nameof(GatedService.Run),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null
        )!;

        // when
        var act = () => checker.CheckAsync(new MethodInvocationFeatureCheckerContext(method), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    private static ServiceProvider _CreateProvider(params string[] enabledFeatures)
    {
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager
            .GetAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                var value = enabledFeatures.Contains(name, StringComparer.Ordinal) ? "true" : "false";

                return new FeatureValue(name, value, Provider: null);
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton(featureManager);
        services.AddTransient<IAuthorizationHandler, FeatureRequirementHandler>();
        services.AddSingleton<IMethodInvocationFeatureCheckerService, MethodInvocationFeatureCheckerService>();

        return services.BuildServiceProvider();
    }

    private static Task<AuthorizationResult> _AuthorizeAsync(IServiceProvider provider, AuthorizationPolicy policy)
    {
        return provider
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), resource: null, policy);
    }

    [RequiresFeature(_Reports)]
    private sealed class GatedService
    {
        public void Run() { }
    }
}
