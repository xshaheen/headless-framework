// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Constants;

public static class FrameworkClaimTypes
{
    public const string AccountId = "account_id";

    public const string UserId = "user_id";

    public const string AccountType = "account_type";

    public const string Name = "name";

    public const string Email = "email";

    public const string UserName = "name";

    public const string EmailVerified = "email_verified";

    public const string PhoneCountryCode = "phone_country_code";

    public const string PhoneNumber = "phone_number";

    public const string PhoneNumberVerified = "phone_number_verified";

    public const string FirstName = "first_name";

    public const string LastName = "last_name";

    public const string FullName = "full_name";

    public const string SecurityStamp = "security_stamp";

    public const string Roles = "role";

    public const string Permission = "permission";

    public const string TenantId = "tenant_id";

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

    /// <summary>The identity provider.</summary>
    public const string IdentityProvider = "idp";

    /// <summary>Authentication Methods References. JSON array of strings that are identifiers for authentication methods used in the authentication.</summary>
    public const string AuthenticationMethod = "amr";

    /// <summary>Session identifier. This represents a Session of an OP at an RP to a User Agent or device for a logged-in End-User. Its contents are unique to the OP and opaque to the RP.</summary>
    public const string SessionId = "sid";
}
