// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace FluentValidation.Resources;

/// <summary>
/// Factory methods that return the <see cref="ErrorDescriptor"/> instances used by the Headless
/// FluentValidation extension rules. Each method returns a descriptor whose code follows the
/// <c>domain:snake_case</c> convention and whose description is drawn from the localized resource
/// file.
/// </summary>
[PublicAPI]
public static class FluentValidatorErrorDescriber
{
    /// <summary>Error descriptors for geographic coordinate validation failures.</summary>
    [PublicAPI]
    public static class Geo
    {
        /// <summary>Returns the descriptor for an invalid longitude value (code <c>geo:invalid_longitude</c>).</summary>
        public static ErrorDescriptor InvalidLongitude()
        {
            return new("geo:invalid_longitude", FluentValidatorErrors.geo_invalid_longitude);
        }

        /// <summary>Returns the descriptor for an invalid latitude value (code <c>geo:invalid_latitude</c>).</summary>
        public static ErrorDescriptor InvalidLatitude()
        {
            return new("geo:invalid_latitude", FluentValidatorErrors.geo_invalid_latitude);
        }
    }

    /// <summary>Error descriptors for string format validation failures.</summary>
    [PublicAPI]
    public static class Strings
    {
        /// <summary>Returns the descriptor for a string that must contain only digits (code <c>strings:only_numbers</c>).</summary>
        public static ErrorDescriptor OnlyNumberValidator()
        {
            return new(code: "strings:only_numbers", description: FluentValidatorErrors.strings_only_numbers);
        }
    }

    /// <summary>Error descriptors for collection validation failures.</summary>
    [PublicAPI]
    public static class Collections
    {
        /// <summary>Returns the descriptor for a collection that exceeds the maximum element count (code <c>collection:maximum_elements</c>).</summary>
        public static ErrorDescriptor MaximumElementsValidator()
        {
            return new("collection:maximum_elements", FluentValidatorErrors.collection_maximum_elements);
        }

        /// <summary>Returns the descriptor for a collection that is below the minimum element count (code <c>collection:minimum_elements</c>).</summary>
        public static ErrorDescriptor MinimumElementsValidator()
        {
            return new("collection:minimum_elements", FluentValidatorErrors.collection_minimum_elements);
        }

        /// <summary>Returns the descriptor for a collection that contains duplicate elements (code <c>collection:unique_elements</c>).</summary>
        public static ErrorDescriptor UniqueElementsValidator()
        {
            return new("collection:unique_elements", FluentValidatorErrors.collection_unique_elements);
        }
    }

    /// <summary>Error descriptors for phone number validation failures.</summary>
    [PublicAPI]
    public static class PhoneNumbers
    {
        /// <summary>Returns the descriptor for an invalid phone number (code <c>phone_number:invalid_number</c>).</summary>
        public static ErrorDescriptor InvalidNumber()
        {
            return new(
                code: "phone_number:invalid_number",
                description: FluentValidatorErrors.phone_number_invalid_number
            );
        }

        /// <summary>Returns the descriptor for a missing or invalid country code (code <c>phone_number:invalid_country_code</c>).</summary>
        public static ErrorDescriptor InvalidNumberCountryCodeValidator()
        {
            return new(
                code: "phone_number:invalid_country_code",
                description: FluentValidatorErrors.phone_number_invalid_country_code
            );
        }

        /// <summary>Returns the descriptor for a local-only phone number that is not valid internationally (code <c>phone_number:local_number</c>).</summary>
        public static ErrorDescriptor NotLocalNumberValidator()
        {
            return new(code: "phone_number:local_number", description: FluentValidatorErrors.phone_number_local_number);
        }
    }

    /// <summary>Error descriptors for national ID validation failures.</summary>
    [PublicAPI]
    public static class NationalIds
    {
        /// <summary>Returns the descriptor for an invalid Egyptian national ID (code <c>national_id:invalid_egyptian_national_id</c>).</summary>
        public static ErrorDescriptor InvalidEgyptianNationalId()
        {
            return new(
                code: "national_id:invalid_egyptian_national_id",
                description: FluentValidatorErrors.national_id_invalid_egyptian_national_id
            );
        }
    }

    /// <summary>Error descriptors for URL and CORS origin validation failures.</summary>
    [PublicAPI]
    public static class Urls
    {
        /// <summary>Returns the descriptor for an invalid URL (code <c>url:invalid</c>).</summary>
        public static ErrorDescriptor InvalidUrl()
        {
            return new(code: "url:invalid", description: FluentValidatorErrors.url_invalid);
        }

        /// <summary>Returns the descriptor for a malformed CORS origin (code <c>url:invalid_origin_format</c>).</summary>
        public static ErrorDescriptor InvalidOriginFormat()
        {
            return new(code: "url:invalid_origin_format", description: FluentValidatorErrors.url_invalid_origin_format);
        }

        /// <summary>Returns the descriptor for a CORS origin whose scheme is not <c>http</c> or <c>https</c> (code <c>url:invalid_origin_scheme</c>).</summary>
        public static ErrorDescriptor InvalidOriginScheme()
        {
            return new(code: "url:invalid_origin_scheme", description: FluentValidatorErrors.url_invalid_origin_scheme);
        }

        /// <summary>Returns the descriptor for a CORS origin that includes a path, query, or fragment (code <c>url:invalid_origin_path</c>).</summary>
        public static ErrorDescriptor InvalidOriginNotRootPath()
        {
            return new(code: "url:invalid_origin_path", description: FluentValidatorErrors.url_invalid_origin_path);
        }

        /// <summary>Returns the descriptor for a CORS origin that ends with a trailing slash (code <c>url:invalid_origin_trailing_slash</c>).</summary>
        public static ErrorDescriptor InvalidOriginTrailingSlash()
        {
            return new(
                code: "url:invalid_origin_trailing_slash",
                description: FluentValidatorErrors.url_invalid_origin_trailing_slash
            );
        }
    }
}
