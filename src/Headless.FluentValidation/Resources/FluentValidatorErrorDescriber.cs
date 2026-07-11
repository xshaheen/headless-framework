// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace FluentValidation.Resources;

/// <summary>
/// Factory methods that return the <see cref="ErrorDescriptor"/> instances used by the Headless
/// FluentValidation extension rules. Each method returns a descriptor whose code follows the
/// framework-standard <c>g:snake_case</c> shape (the <c>g:</c> prefix marks the shared "general"
/// descriptor space) and whose description is drawn from the localized resource file. The nested
/// classes group the descriptors by validation domain; the domain is an organizational concern
/// only and is not encoded in the emitted error code.
/// </summary>
[PublicAPI]
public static class FluentValidatorErrorDescriber
{
    /// <summary>Error descriptors for geographic coordinate validation failures.</summary>
    [PublicAPI]
    public static class Geo
    {
        /// <summary>Returns the descriptor for an invalid longitude value (code <c>g:invalid_longitude</c>).</summary>
        public static ErrorDescriptor InvalidLongitude()
        {
            return new("g:invalid_longitude", FluentValidatorErrors.g_invalid_longitude);
        }

        /// <summary>Returns the descriptor for an invalid latitude value (code <c>g:invalid_latitude</c>).</summary>
        public static ErrorDescriptor InvalidLatitude()
        {
            return new("g:invalid_latitude", FluentValidatorErrors.g_invalid_latitude);
        }
    }

    /// <summary>Error descriptors for string format validation failures.</summary>
    [PublicAPI]
    public static class Strings
    {
        /// <summary>Returns the descriptor for a string that must contain only digits (code <c>g:only_numbers</c>).</summary>
        public static ErrorDescriptor OnlyNumberValidator()
        {
            return new(code: "g:only_numbers", description: FluentValidatorErrors.g_only_numbers);
        }

        /// <summary>Returns the descriptor for a string that must contain only a decimal number (code <c>g:only_decimals</c>).</summary>
        public static ErrorDescriptor OnlyDecimalsValidator()
        {
            return new(code: "g:only_decimals", description: FluentValidatorErrors.g_only_decimals);
        }

        /// <summary>Returns the descriptor for an invalid URL slug (code <c>g:invalid_slug</c>).</summary>
        public static ErrorDescriptor InvalidSlug()
        {
            return new(code: "g:invalid_slug", description: FluentValidatorErrors.g_invalid_slug);
        }

        /// <summary>Returns the descriptor for an invalid username (code <c>g:invalid_username</c>).</summary>
        public static ErrorDescriptor InvalidUsername()
        {
            return new(code: "g:invalid_username", description: FluentValidatorErrors.g_invalid_username);
        }

        /// <summary>Returns the descriptor for an invalid postal/ZIP code (code <c>g:invalid_zip_code</c>).</summary>
        public static ErrorDescriptor InvalidZipCode()
        {
            return new(code: "g:invalid_zip_code", description: FluentValidatorErrors.g_invalid_zip_code);
        }

        /// <summary>Returns the descriptor for an invalid hex color (code <c>g:invalid_hex_color</c>).</summary>
        public static ErrorDescriptor InvalidHexColor()
        {
            return new(code: "g:invalid_hex_color", description: FluentValidatorErrors.g_invalid_hex_color);
        }

        /// <summary>Returns the descriptor for an invalid Base64 string (code <c>g:invalid_base64</c>).</summary>
        public static ErrorDescriptor InvalidBase64()
        {
            return new(code: "g:invalid_base64", description: FluentValidatorErrors.g_invalid_base64);
        }

        /// <summary>Returns the descriptor for a string with leading or trailing whitespace (code <c>g:not_trimmed</c>).</summary>
        public static ErrorDescriptor NotTrimmed()
        {
            return new(code: "g:not_trimmed", description: FluentValidatorErrors.g_not_trimmed);
        }

        /// <summary>Returns the descriptor for an invalid culture/locale name (code <c>g:invalid_culture</c>).</summary>
        public static ErrorDescriptor InvalidCulture()
        {
            return new(code: "g:invalid_culture", description: FluentValidatorErrors.g_invalid_culture);
        }

        /// <summary>Returns the descriptor for a string that contains a <c>&lt;script&gt;</c> element (code <c>g:contains_scripts</c>).</summary>
        public static ErrorDescriptor ContainsScripts()
        {
            return new(code: "g:contains_scripts", description: FluentValidatorErrors.g_contains_scripts);
        }
    }

    /// <summary>Error descriptors for network value validation failures.</summary>
    [PublicAPI]
    public static class Network
    {
        /// <summary>Returns the descriptor for an invalid IPv4 address (code <c>g:invalid_ipv4</c>).</summary>
        public static ErrorDescriptor InvalidIpv4()
        {
            return new(code: "g:invalid_ipv4", description: FluentValidatorErrors.g_invalid_ipv4);
        }

        /// <summary>Returns the descriptor for an invalid IPv6 address (code <c>g:invalid_ipv6</c>).</summary>
        public static ErrorDescriptor InvalidIpv6()
        {
            return new(code: "g:invalid_ipv6", description: FluentValidatorErrors.g_invalid_ipv6);
        }

        /// <summary>Returns the descriptor for an invalid IP address (code <c>g:invalid_ip</c>).</summary>
        public static ErrorDescriptor InvalidIp()
        {
            return new(code: "g:invalid_ip", description: FluentValidatorErrors.g_invalid_ip);
        }
    }

    /// <summary>Error descriptors for enum name validation failures.</summary>
    [PublicAPI]
    public static class Enums
    {
        /// <summary>Returns the descriptor for a string that is not a defined enum member name (code <c>g:invalid_enum_name</c>).</summary>
        public static ErrorDescriptor InvalidName()
        {
            return new(code: "g:invalid_enum_name", description: FluentValidatorErrors.g_invalid_enum_name);
        }
    }

    /// <summary>Error descriptors for relative date/time validation failures.</summary>
    [PublicAPI]
    public static class DateTimes
    {
        /// <summary>Returns the descriptor for a value that must be in the past (code <c>g:must_be_in_past</c>).</summary>
        public static ErrorDescriptor MustBeInPast()
        {
            return new(code: "g:must_be_in_past", description: FluentValidatorErrors.g_must_be_in_past);
        }

        /// <summary>Returns the descriptor for a value that must be in the future (code <c>g:must_be_in_future</c>).</summary>
        public static ErrorDescriptor MustBeInFuture()
        {
            return new(code: "g:must_be_in_future", description: FluentValidatorErrors.g_must_be_in_future);
        }

        /// <summary>Returns the descriptor for a value that must not be in the past (code <c>g:must_not_be_in_past</c>).</summary>
        public static ErrorDescriptor MustNotBeInPast()
        {
            return new(code: "g:must_not_be_in_past", description: FluentValidatorErrors.g_must_not_be_in_past);
        }

        /// <summary>Returns the descriptor for a value that must not be in the future (code <c>g:must_not_be_in_future</c>).</summary>
        public static ErrorDescriptor MustNotBeInFuture()
        {
            return new(code: "g:must_not_be_in_future", description: FluentValidatorErrors.g_must_not_be_in_future);
        }

        /// <summary>Returns the descriptor for a date that does not meet the minimum age requirement (code <c>g:minimum_age</c>).</summary>
        public static ErrorDescriptor MinimumAge()
        {
            return new(code: "g:minimum_age", description: FluentValidatorErrors.g_minimum_age);
        }
    }

    /// <summary>Error descriptors for collection validation failures.</summary>
    [PublicAPI]
    public static class Collections
    {
        /// <summary>Returns the descriptor for a collection that exceeds the maximum element count (code <c>g:maximum_elements</c>).</summary>
        public static ErrorDescriptor MaximumElementsValidator()
        {
            return new("g:maximum_elements", FluentValidatorErrors.g_maximum_elements);
        }

        /// <summary>Returns the descriptor for a collection that is below the minimum element count (code <c>g:minimum_elements</c>).</summary>
        public static ErrorDescriptor MinimumElementsValidator()
        {
            return new("g:minimum_elements", FluentValidatorErrors.g_minimum_elements);
        }

        /// <summary>Returns the descriptor for a collection that contains duplicate elements (code <c>g:unique_elements</c>).</summary>
        public static ErrorDescriptor UniqueElementsValidator()
        {
            return new("g:unique_elements", FluentValidatorErrors.g_unique_elements);
        }
    }

    /// <summary>Error descriptors for phone number validation failures.</summary>
    [PublicAPI]
    public static class PhoneNumbers
    {
        /// <summary>Returns the descriptor for an invalid phone number (code <c>g:invalid_phone_number</c>).</summary>
        public static ErrorDescriptor InvalidNumber()
        {
            return new(code: "g:invalid_phone_number", description: FluentValidatorErrors.g_invalid_phone_number);
        }

        /// <summary>Returns the descriptor for a missing or invalid country code (code <c>g:invalid_country_code</c>).</summary>
        public static ErrorDescriptor InvalidNumberCountryCodeValidator()
        {
            return new(code: "g:invalid_country_code", description: FluentValidatorErrors.g_invalid_country_code);
        }

        /// <summary>Returns the descriptor for a local-only phone number that is not valid internationally (code <c>g:local_phone_number</c>).</summary>
        public static ErrorDescriptor NotLocalNumberValidator()
        {
            return new(code: "g:local_phone_number", description: FluentValidatorErrors.g_local_phone_number);
        }

        /// <summary>Returns the descriptor for a number that is not a valid mobile phone number (code <c>g:invalid_mobile_number</c>).</summary>
        public static ErrorDescriptor InvalidMobileNumber()
        {
            return new(code: "g:invalid_mobile_number", description: FluentValidatorErrors.g_invalid_mobile_number);
        }
    }

    /// <summary>Error descriptors for national ID validation failures.</summary>
    [PublicAPI]
    public static class NationalIds
    {
        /// <summary>Returns the descriptor for an invalid Egyptian national ID (code <c>g:invalid_egyptian_national_id</c>).</summary>
        public static ErrorDescriptor InvalidEgyptianNationalId()
        {
            return new(
                code: "g:invalid_egyptian_national_id",
                description: FluentValidatorErrors.g_invalid_egyptian_national_id
            );
        }
    }

    /// <summary>Error descriptors for URL and CORS origin validation failures.</summary>
    [PublicAPI]
    public static class Urls
    {
        /// <summary>Returns the descriptor for an invalid URL (code <c>g:invalid_url</c>).</summary>
        public static ErrorDescriptor InvalidUrl()
        {
            return new(code: "g:invalid_url", description: FluentValidatorErrors.g_invalid_url);
        }

        /// <summary>Returns the descriptor for a malformed CORS origin (code <c>g:invalid_origin_format</c>).</summary>
        public static ErrorDescriptor InvalidOriginFormat()
        {
            return new(code: "g:invalid_origin_format", description: FluentValidatorErrors.g_invalid_origin_format);
        }

        /// <summary>Returns the descriptor for a CORS origin whose scheme is not <c>http</c> or <c>https</c> (code <c>g:invalid_origin_scheme</c>).</summary>
        public static ErrorDescriptor InvalidOriginScheme()
        {
            return new(code: "g:invalid_origin_scheme", description: FluentValidatorErrors.g_invalid_origin_scheme);
        }

        /// <summary>Returns the descriptor for a CORS origin that includes a path, query, or fragment (code <c>g:invalid_origin_path</c>).</summary>
        public static ErrorDescriptor InvalidOriginNotRootPath()
        {
            return new(code: "g:invalid_origin_path", description: FluentValidatorErrors.g_invalid_origin_path);
        }

        /// <summary>Returns the descriptor for a CORS origin that ends with a trailing slash (code <c>g:invalid_origin_trailing_slash</c>).</summary>
        public static ErrorDescriptor InvalidOriginTrailingSlash()
        {
            return new(
                code: "g:invalid_origin_trailing_slash",
                description: FluentValidatorErrors.g_invalid_origin_trailing_slash
            );
        }
    }
}
