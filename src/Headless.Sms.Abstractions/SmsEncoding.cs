// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Specifies the character encoding a carrier uses for an SMS text.</summary>
[PublicAPI]
public enum SmsEncoding
{
    /// <summary>The GSM 03.38 7-bit alphabet: 160 characters in one part, 153 per part when split.</summary>
    Gsm7 = 0,

    /// <summary>UCS-2 for any text outside the GSM alphabet: 70 characters in one part, 67 per part when split.</summary>
    Ucs2 = 1,
}
