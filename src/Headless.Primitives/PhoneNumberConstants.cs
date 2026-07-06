// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Default column names and length constraints for storing phone country codes and national numbers.</summary>
public static class PhoneNumberConstants
{
    /// <summary>Constraints for the phone country-code component.</summary>
    public static class Codes
    {
        /// <summary>Default database column name for the country code.</summary>
        public const string DefaultColumnName = "PhoneCountryCode";

        /// <summary>Minimum length of the country code.</summary>
        public const int MinLength = 1;

        /// <summary>Maximum length of the country code.</summary>
        public const int MaxLength = 10;
    }

    /// <summary>Constraints for the national phone-number component.</summary>
    public static class Numbers
    {
        /// <summary>Default database column name for the national number.</summary>
        public const string DefaultColumnName = "PhoneNumber";

        /// <summary>Minimum length of the national number.</summary>
        public const int MinLength = 5;

        /// <summary>Maximum length of the national number.</summary>
        public const int MaxLength = 30;
    }
}
