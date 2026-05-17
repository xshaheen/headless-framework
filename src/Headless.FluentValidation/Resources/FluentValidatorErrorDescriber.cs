// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace FluentValidation.Resources;

public static class FluentValidatorErrorDescriber
{
    public static class Geo
    {
        public static ErrorDescriptor InvalidLongitude()
        {
            return new("geo:invalid_longitude", FluentValidatorErrors.geo_invalid_longitude);
        }

        public static ErrorDescriptor InvalidLatitude()
        {
            return new("geo:invalid_latitude", FluentValidatorErrors.geo_invalid_latitude);
        }
    }

    public static class Strings
    {
        public static ErrorDescriptor OnlyNumberValidator()
        {
            return new(code: "strings:only_number", description: FluentValidatorErrors.strings_only_numbers);
        }
    }

    public static class Collections
    {
        public static ErrorDescriptor MaximumElementsValidator()
        {
            return new("collection:minimum_elements", FluentValidatorErrors.collection_maximum_elements);
        }

        public static ErrorDescriptor MinimumElementsValidator()
        {
            return new("collection:minimum_elements", FluentValidatorErrors.collection_minimum_elements);
        }

        public static ErrorDescriptor UniqueElementsValidator()
        {
            return new("collection:unique_elements", FluentValidatorErrors.collection_unique_elements);
        }
    }

    public static class PhoneNumbers
    {
        public static ErrorDescriptor InvalidNumber()
        {
            return new(
                code: "phone_number:invalid_number",
                description: FluentValidatorErrors.phone_number_invalid_number
            );
        }

        public static ErrorDescriptor InvalidNumberCountryCodeValidator()
        {
            return new(
                code: "phone_number:invalid_country_code",
                description: FluentValidatorErrors.phone_number_invalid_country_code
            );
        }

        public static ErrorDescriptor NotLocalNumberValidator()
        {
            return new(code: "phone_number:local_number", description: FluentValidatorErrors.phone_number_local_number);
        }
    }

    public static class NationalIds
    {
        public static ErrorDescriptor InvalidEgyptianNationalId()
        {
            return new(
                code: "national_id:invalid_egyptian_national_id",
                description: FluentValidatorErrors.national_id_invalid_egyptian_national_id
            );
        }
    }

    public static class Urls
    {
        public static ErrorDescriptor InvalidUrl()
        {
            return new(code: "url:invalid", description: FluentValidatorErrors.url_invalid);
        }

        public static ErrorDescriptor InvalidOriginFormat()
        {
            return new(code: "url:invalid_origin_format", description: FluentValidatorErrors.url_invalid_origin_format);
        }

        public static ErrorDescriptor InvalidOriginScheme()
        {
            return new(code: "url:invalid_origin_scheme", description: FluentValidatorErrors.url_invalid_origin_scheme);
        }

        public static ErrorDescriptor InvalidOriginNotRootPath()
        {
            return new(code: "url:invalid_origin_path", description: FluentValidatorErrors.url_invalid_origin_path);
        }

        public static ErrorDescriptor InvalidOriginTrailingSlash()
        {
            return new(
                code: "url:invalid_origin_trailing_slash",
                description: FluentValidatorErrors.url_invalid_origin_trailing_slash
            );
        }
    }
}
