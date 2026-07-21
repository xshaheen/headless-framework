// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.FluentValidation.Resources;

/// <summary>
/// Compile-time constants for the <c>errors[].code</c> values emitted by the Headless FluentValidation
/// extension rules (see <see cref="FluentValidatorErrorDescriber"/>). All codes follow the framework-standard
/// <c>g:snake_case</c> shape (the <c>g:</c> prefix marks the shared "general" descriptor space). Clients should
/// branch on these constants rather than inspect the human-readable description, which is localized.
/// </summary>
[PublicAPI]
public static class FluentValidatorErrorCodes
{
    // Geographic coordinates

    /// <summary>An invalid longitude value.</summary>
    public const string InvalidLongitude = "g:invalid_longitude";

    /// <summary>An invalid latitude value.</summary>
    public const string InvalidLatitude = "g:invalid_latitude";

    // Strings

    /// <summary>A string that must contain only digits.</summary>
    public const string OnlyNumbers = "g:only_numbers";

    /// <summary>A string that must contain only a decimal number.</summary>
    public const string OnlyDecimals = "g:only_decimals";

    /// <summary>An invalid URL slug.</summary>
    public const string InvalidSlug = "g:invalid_slug";

    /// <summary>An invalid username.</summary>
    public const string InvalidUsername = "g:invalid_username";

    /// <summary>An invalid postal/ZIP code.</summary>
    public const string InvalidZipCode = "g:invalid_zip_code";

    /// <summary>An invalid hex color.</summary>
    public const string InvalidHexColor = "g:invalid_hex_color";

    /// <summary>An invalid Base64 string.</summary>
    public const string InvalidBase64 = "g:invalid_base64";

    /// <summary>A string with leading or trailing whitespace.</summary>
    public const string NotTrimmed = "g:not_trimmed";

    /// <summary>An invalid culture/locale name.</summary>
    public const string InvalidCulture = "g:invalid_culture";

    /// <summary>A string that contains a <c>&lt;script&gt;</c> element.</summary>
    public const string ContainsScripts = "g:contains_scripts";

    // Network

    /// <summary>An invalid IPv4 address.</summary>
    public const string InvalidIpv4 = "g:invalid_ipv4";

    /// <summary>An invalid IPv6 address.</summary>
    public const string InvalidIpv6 = "g:invalid_ipv6";

    /// <summary>An invalid IP address.</summary>
    public const string InvalidIp = "g:invalid_ip";

    // Enums

    /// <summary>A string that is not a defined enum member name.</summary>
    public const string InvalidEnumName = "g:invalid_enum_name";

    // Relative date/time

    /// <summary>A value that must be in the past.</summary>
    public const string MustBeInPast = "g:must_be_in_past";

    /// <summary>A value that must be in the future.</summary>
    public const string MustBeInFuture = "g:must_be_in_future";

    /// <summary>A value that must not be in the past.</summary>
    public const string MustNotBeInPast = "g:must_not_be_in_past";

    /// <summary>A value that must not be in the future.</summary>
    public const string MustNotBeInFuture = "g:must_not_be_in_future";

    /// <summary>A date that does not meet the minimum age requirement.</summary>
    public const string MinimumAge = "g:minimum_age";

    // Collections

    /// <summary>A collection that exceeds the maximum element count.</summary>
    public const string MaximumElements = "g:maximum_elements";

    /// <summary>A collection that is below the minimum element count.</summary>
    public const string MinimumElements = "g:minimum_elements";

    /// <summary>A collection that contains duplicate elements.</summary>
    public const string UniqueElements = "g:unique_elements";

    // Phone numbers

    /// <summary>An invalid phone number.</summary>
    public const string InvalidPhoneNumber = "g:invalid_phone_number";

    /// <summary>A missing or invalid country code.</summary>
    public const string InvalidCountryCode = "g:invalid_country_code";

    /// <summary>A local-only phone number that is not valid internationally.</summary>
    public const string LocalPhoneNumber = "g:local_phone_number";

    /// <summary>A number that is not a valid mobile phone number.</summary>
    public const string InvalidMobileNumber = "g:invalid_mobile_number";

    // National IDs

    /// <summary>An invalid Egyptian national ID.</summary>
    public const string InvalidEgyptianNationalId = "g:invalid_egyptian_national_id";

    // URLs and CORS origins

    /// <summary>An invalid URL.</summary>
    public const string InvalidUrl = "g:invalid_url";

    /// <summary>A malformed CORS origin.</summary>
    public const string InvalidOriginFormat = "g:invalid_origin_format";

    /// <summary>A CORS origin whose scheme is not <c>http</c> or <c>https</c>.</summary>
    public const string InvalidOriginScheme = "g:invalid_origin_scheme";

    /// <summary>A CORS origin that includes a path, query, or fragment.</summary>
    public const string InvalidOriginPath = "g:invalid_origin_path";

    /// <summary>A CORS origin that ends with a trailing slash.</summary>
    public const string InvalidOriginTrailingSlash = "g:invalid_origin_trailing_slash";
}
