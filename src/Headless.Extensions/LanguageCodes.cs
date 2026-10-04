// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Two-letter language codes for the languages the framework recognizes. Most values are ISO 639-1
/// codes, but a few deviate: <see cref="Korean"/> is <c>"kr"</c> (ISO 639-1 is <c>ko</c>) and
/// <see cref="Chinese"/> is <c>"cn"</c> (ISO 639-1 is <c>zh</c>). Treat these as framework-internal
/// identifiers rather than guaranteed ISO 639-1 codes.
/// </summary>
[PublicAPI]
public static class LanguageCodes
{
    public const string English = "en";
    public const string Arabic = "ar";
    public const string Korean = "kr";
    public const string Portuguese = "pt";
    public const string Dutch = "nl";
    public const string Croatian = "hr";
    public const string Persian = "fa";
    public const string German = "de";
    public const string Spanish = "es";
    public const string French = "fr";
    public const string Japanese = "ja";
    public const string Italian = "it";
    public const string Chinese = "cn";
    public const string Turkish = "tr";
}
