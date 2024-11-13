// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Kernel.BuildingBlocks.Models.Primitives;

public static class PhoneNumberConstants
{
    public static class Codes
    {
        public const string DefaultColumnName = "PhoneCountryCode";
        public const int MinLength = 1;
        public const int MaxLength = 10;
    }

    public static class Numbers
    {
        public const string DefaultColumnName = "PhoneNumber";
        public const int MinLength = 5;
        public const int MaxLength = 30;
    }
}
