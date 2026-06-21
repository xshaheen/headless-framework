// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Framework user/profile claim type names (the <c>snake_case</c> claim keys carried in identity
/// tokens and <see cref="System.Security.Claims.ClaimsPrincipal"/> instances). Several keys follow
/// OpenID Connect standard claim names (<c>updated_at</c>, <c>zoneinfo</c>, <c>locale</c>, etc.);
/// the rest are framework conventions. Note <see cref="Name"/> and <see cref="UserName"/> both map
/// to <c>"name"</c>, and <see cref="Roles"/> maps to the singular key <c>"role"</c>. For the
/// registered JWT/OIDC claim keys (<c>sub</c>, <c>iss</c>, …) see <see cref="JwtClaimTypes"/>.
/// </summary>
[PublicAPI]
public static class UserClaimTypes
{
    /// <summary>The account identifier claim (<c>account_id</c>).</summary>
    public const string AccountId = "account_id";

    /// <summary>The user identifier claim (<c>user_id</c>).</summary>
    public const string UserId = "user_id";

    /// <summary>The account type claim (<c>account_type</c>), e.g. to distinguish user categories.</summary>
    public const string AccountType = "account_type";

    /// <summary>The display name claim (<c>name</c>). Alias of <see cref="UserName"/>.</summary>
    public const string Name = "name";

    /// <summary>The email address claim (<c>email</c>).</summary>
    public const string Email = "email";

    /// <summary>The username claim (<c>name</c>). Alias of <see cref="Name"/>.</summary>
    public const string UserName = "name";

    /// <summary>Flag claim indicating whether the email address has been verified (<c>email_verified</c>).</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>The phone country calling code claim (<c>phone_country_code</c>).</summary>
    public const string PhoneCountryCode = "phone_country_code";

    /// <summary>The phone number claim (<c>phone_number</c>).</summary>
    public const string PhoneNumber = "phone_number";

    /// <summary>Flag claim indicating whether the phone number has been verified (<c>phone_number_verified</c>).</summary>
    public const string PhoneNumberVerified = "phone_number_verified";

    /// <summary>The user's first (given) name claim (<c>first_name</c>).</summary>
    public const string FirstName = "first_name";

    /// <summary>The user's last (family) name claim (<c>last_name</c>).</summary>
    public const string LastName = "last_name";

    /// <summary>The user's full display name claim (<c>full_name</c>).</summary>
    public const string FullName = "full_name";

    /// <summary>The security stamp claim (<c>security_stamp</c>), used to invalidate tokens when credentials change.</summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>The role claim key (<c>role</c>); a principal may carry multiple role claims with this type.</summary>
    public const string Roles = "role";

    /// <summary>The permission claim key (<c>permission</c>); used for fine-grained authorization.</summary>
    public const string Permission = "permission";

    /// <summary>The tenant identifier claim (<c>tenant_id</c>), used in multi-tenant deployments.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>The edition (subscription tier) identifier claim (<c>edition_id</c>).</summary>
    public const string EditionId = "edition_id";

    /// <summary>Time the End-User's information was last updated. Its value is a JSON number representing the number of seconds from 1970-01-01T0:0:0Z as measured in UTC until the date/time.</summary>
    public const string UpdatedAt = "updated_at";

    /// <summary>String from the time zone database (http://www.twinsun.com/tz/tz-link.htm) representing the End-User's time zone. For example, Europe/Paris or America/Los_Angeles.</summary>
    public const string ZoneInfo = "zoneinfo";

    /// <summary>
    /// End-User's locale, represented as a BCP47 [RFC5646] language tag.
    /// This is typically an ISO 639-1 Alpha-2 [ISO639‑1] language code in lowercase and an ISO 3166-1 Alpha-2 [ISO3166‑1] country code in uppercase, separated by a dash.
    /// For example, en-US or fr-CA. As a compatibility note, some implementations have used an underscore as the separator rather than a dash, for example, en_US;
    /// Relying on Parties MAY choose to accept this locale syntax as well.
    /// </summary>
    public const string Locale = "locale";
}
