// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Claims;
using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Authorization;

public sealed class HeadlessAuthorizationPolicyBuilderExtensionsTests : TestBase
{
    #region Argument Validation

    [Fact]
    public void should_throw_argument_null_exception_when_builder_is_null_for_require_single_claim_value()
    {
        // given
        AuthorizationPolicyBuilder builder = null!;

        // when
        var act = () => builder.RequireSingleClaimValue("tier", "gold");

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void should_throw_argument_exception_when_claim_type_is_null_or_whitespace_for_require_single_claim_value(
        string? claimType
    )
    {
        // given
        var builder = new AuthorizationPolicyBuilder();

        // when
        var act = () => builder.RequireSingleClaimValue(claimType!, "gold");

        // then
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(claimType));
    }

    [Fact]
    public void should_throw_argument_null_exception_when_value_is_null_for_require_single_claim_value()
    {
        // given
        var builder = new AuthorizationPolicyBuilder();

        // when
        var act = () => builder.RequireSingleClaimValue("tier", null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void should_throw_argument_null_exception_when_builder_is_null_for_require_single_claim()
    {
        // given
        AuthorizationPolicyBuilder builder = null!;

        // when
        var act = () => builder.RequireSingleClaim("tier", _ => true);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void should_throw_argument_exception_when_claim_type_is_null_or_whitespace_for_require_single_claim(
        string? claimType
    )
    {
        // given
        var builder = new AuthorizationPolicyBuilder();

        // when
        var act = () => builder.RequireSingleClaim(claimType!, _ => true);

        // then
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(claimType));
    }

    [Fact]
    public void should_throw_argument_null_exception_when_predicate_is_null_for_require_single_claim()
    {
        // given
        var builder = new AuthorizationPolicyBuilder();

        // when
        var act = () => builder.RequireSingleClaim("tier", null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    #endregion

    #region RequireSingleClaimValue

    [Fact]
    public async Task should_fail_when_principal_has_zero_claims_of_type()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("unrelated", "value")], "TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_succeed_when_principal_has_exactly_one_matching_claim()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("unrelated", "value"), new Claim("tier", "gold")], "TestAuth")
        );

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_principal_has_one_non_matching_claim()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tier", "silver")], "TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_principal_has_one_claim_with_different_case()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tier", "Gold")], "TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_principal_has_two_claims_including_a_match()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("tier", "gold"), new Claim("tier", "silver")], "TestAuth")
        );

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_principal_has_two_claims_both_matching()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("tier", "gold"), new Claim("tier", "gold")], "TestAuth")
        );

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_principal_has_multiple_identities_with_multiple_claims_of_same_type()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var identity1 = new ClaimsIdentity([new Claim("tier", "gold")], "Auth1");
        var identity2 = new ClaimsIdentity([new Claim("tier", "bronze")], "Auth2");
        var principal = new ClaimsPrincipal([identity1, identity2]);

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_principal_is_empty_or_unauthenticated()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaimValue("tier", "gold").Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal();

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region RequireSingleClaim with Predicate

    [Fact]
    public async Task should_succeed_when_single_claim_satisfies_predicate()
    {
        // given
        var policy = new AuthorizationPolicyBuilder()
            .RequireSingleClaim(
                "age",
                value => int.TryParse(value, CultureInfo.InvariantCulture, out var age) && age >= 18
            )
            .Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("age", "25")], "TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_single_claim_does_not_satisfy_predicate()
    {
        // given
        var policy = new AuthorizationPolicyBuilder()
            .RequireSingleClaim(
                "age",
                value => int.TryParse(value, CultureInfo.InvariantCulture, out var age) && age >= 18
            )
            .Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("age", "16")], "TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_predicate_form_has_two_claims_of_type()
    {
        // given
        var policy = new AuthorizationPolicyBuilder()
            .RequireSingleClaim(
                "age",
                value => int.TryParse(value, CultureInfo.InvariantCulture, out var age) && age >= 18
            )
            .Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("age", "25"), new Claim("age", "30")], "TestAuth")
        );

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_when_predicate_form_has_zero_claims()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireSingleClaim("age", _ => true).Build();

        var authService = _BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

        // when
        var result = await authService.AuthorizeAsync(principal, policy);

        // then
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Requirement Properties & ToString

    [Fact]
    public void should_set_properties_and_to_string_with_exact_value()
    {
        // given
        var requirement = new SingleClaimRequirement(
            "tier",
            v => string.Equals(v, "gold", StringComparison.Ordinal),
            "gold"
        );

        // then
        requirement.ClaimType.Should().Be("tier");
        requirement.RequiredValue.Should().Be("gold");
        requirement.ToString().Should().Be("SingleClaimRequirement: Claim.Type=tier and Claim.Value=gold");
    }

    [Fact]
    public void should_set_properties_and_to_string_with_predicate_only()
    {
        // given
        var requirement = new SingleClaimRequirement("tier", v => string.Equals(v, "gold", StringComparison.Ordinal));

        // then
        requirement.ClaimType.Should().Be("tier");
        requirement.RequiredValue.Should().BeNull();
        requirement.ToString().Should().Be("SingleClaimRequirement: Claim.Type=tier");
    }

    #endregion

    private static IAuthorizationService _BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IAuthorizationService>();
    }
}
