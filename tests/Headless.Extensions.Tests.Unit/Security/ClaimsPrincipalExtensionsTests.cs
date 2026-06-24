// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;

namespace Tests.Security;

public sealed class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void get_roles_should_match_role_claim_type_case_insensitively()
    {
        // given a principal whose role claims use differently-cased claim types
        var identity = new ClaimsIdentity([
            new Claim(UserClaimTypes.Roles, "admin"), // "role"
            new Claim("ROLE", "manager"), // case-differing type, previously dropped with Ordinal
        ]);

        var principal = new ClaimsPrincipal(identity);

        // when
        var roles = principal.GetRoles();

        // then both role values are returned (matching FindFirst's OrdinalIgnoreCase semantics)
        roles.Should().BeEquivalentTo("admin", "manager");
    }

    [Fact]
    public void get_roles_should_preserve_value_case_sensitivity()
    {
        // given two role values differing only by case
        var identity = new ClaimsIdentity([
            new Claim(UserClaimTypes.Roles, "Admin"),
            new Claim(UserClaimTypes.Roles, "admin"),
        ]);

        var principal = new ClaimsPrincipal(identity);

        // when
        var roles = principal.GetRoles();

        // then both distinct (case-sensitive) values are retained
        roles.Should().BeEquivalentTo("Admin", "admin");
    }

    [Fact]
    public void get_roles_should_return_empty_for_null_principal()
    {
        // given
        ClaimsPrincipal? principal = null;

        // when / then
        principal.GetRoles().Should().BeEmpty();
    }
}
